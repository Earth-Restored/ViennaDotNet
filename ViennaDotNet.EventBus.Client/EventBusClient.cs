using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.EventBus.Client
{
    public class EventBusClient
    {
        public static EventBusClient create(string connectionString)
        {
            string[] parts = connectionString.Split(':', 2);
            string host = parts[0];
            int port;
            try
            {
                port = parts.Length > 1 ? int.Parse(parts[1]) : 5532;
            }
            catch (Exception)
            {
                throw new ArgumentException($"Invalid port number \"{parts[1]}\"", nameof(connectionString));
            }

            if (port <= 0 || port > 65535)
                throw new ArgumentException("Port number out of range", nameof(connectionString));

            Socket socket;
            try
            {
                socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                socket.Connect(host, port);
            }
            catch (IOException exception)
            {
                throw new ConnectException("Could not create socket", exception);
            }

            return new EventBusClient(socket);
        }

        public sealed class ConnectException : EventBusClientException
        {
            public ConnectException(string? message)
                : base(message)
            {
            }
            public ConnectException(string? message, Exception? innerException)
                : base(message, innerException)
            {
            }
        }

        private Socket socket;
        private readonly BlockingCollection<string> outgoingMessageQueue = new();
        private Thread outgoingThread;
        private Thread incomingThread;
        private readonly ReaderWriterLockSlim lockObj = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        private bool closed = false;
        private bool error = false;

        private Dictionary<int, Publisher> publishers = new();
        private Dictionary<int, Subscriber> subscribers = new();
        private int nextChannelId = 1;

        private EventBusClient(Socket socket)
        {
            this.socket = socket;

            outgoingThread = new Thread(() =>
            {

                try
                {
                    while (true)
                    {
                        if (outgoingMessageQueue.Count > 0)
                        {
                            string message = outgoingMessageQueue.Take();
                            byte[] bytes = Encoding.ASCII.GetBytes(message);
                            socket.Send(bytes);
                        }
                        Thread.Sleep(0);
                    }
                }
                catch (ThreadAbortException)
                {
                    // empty
                }
                catch (IOException)
                {
                    lockObj.EnterWriteLock();
                    error = true;
                    lockObj.ExitWriteLock();
                }
                initiateClose();

                publishers.ForEach((channelId, publisher) =>
                {
                    publisher.closed();
                });
                publishers.Clear();
            });

            incomingThread = new Thread(() =>
            {
                try
                {
                    byte[] readBuffer = new byte[1024];
                    MemoryStream byteArrayOutputStream = new MemoryStream(1024);
                    for (; ; )
                    {
                        int readLength = socket.Receive(readBuffer);
                        if (readLength > 0)
                        {
                            int startOffset = 0;
                            for (int offset = 0; offset < readLength; offset++)
                            {
                                if (readBuffer[offset] == '\n')
                                {
                                    byteArrayOutputStream.Write(readBuffer, startOffset, offset - startOffset);
                                    string message = Encoding.ASCII.GetString(byteArrayOutputStream.ToArray());

                                    Log.Debug($"REC MSG: {message}");

                                    lockObj.EnterReadLock();
                                    bool suppress = closed || error;
                                    lockObj.ExitReadLock();

                                    if (!suppress)
                                    {
                                        if (!dispatchReceivedMessage(message))
                                        {
                                            lockObj.EnterWriteLock();
                                            error = true;
                                            lockObj.ExitWriteLock();
                                            initiateClose();
                                        }
                                    }
                                    byteArrayOutputStream = new MemoryStream(1024);
                                    startOffset = offset + 1;
                                }
                            }
                            byteArrayOutputStream.Write(readBuffer, startOffset, readLength - startOffset);
                        }
                        else if (readLength == -1)
                            break;
                        else
                            throw new InvalidOperationException();
                    }
                }

                catch (IOException)
                {
                    lockObj.EnterWriteLock();
                    error = true;
                    lockObj.ExitWriteLock();
                }
                initiateClose();

                subscribers.ForEach((channelId, subscriber) =>
                {
                    subscriber.error();
                });
                subscribers.Clear();
            });

            outgoingThread.Start();
            incomingThread.Start();
        }

        public void close()
        {
            initiateClose();

            for (; ; )
            {
                try
                {
                    incomingThread.Join();
                    break;
                }
                catch (ThreadAbortException)
                {
                    // empty
                }
            }

            for (; ; )
            {
                try
                {
                    outgoingThread.Join();
                    break;
                }
                catch (ThreadAbortException)
                {
                    // empty
                }
            }
        }

        private void initiateClose()
        {
            lockObj.EnterWriteLock();
            if (!error)
                closed = true;

            lockObj.ExitWriteLock();

            try
            {
                socket.Close();
            }
            catch (IOException)
            {
                // empty
            }

#pragma warning disable SYSLIB0006 // Type or member is obsolete
            outgoingThread.Abort();
#pragma warning restore SYSLIB0006 // Type or member is obsolete
        }

        public Publisher addPublisher()
        {
            lockObj.EnterWriteLock();
            int channelId = getUnusedChannelId();
            Publisher publisher = new Publisher(this, channelId);
            if (sendMessage(channelId, "PUB"))
                publishers[channelId] = publisher;
            else
                publisher.closed();

            lockObj.ExitWriteLock();

            return publisher;
        }

        public Subscriber addSubscriber(string queueName, Subscriber.ISubscriberListener listener)
        {
            lockObj.EnterWriteLock();
            int channelId = getUnusedChannelId();
            Subscriber subscriber = new Subscriber(this, channelId, queueName, listener);
            if (sendMessage(channelId, "SUB " + queueName))
                subscribers[channelId] = subscriber;
            else
                subscriber.error();

            lockObj.ExitWriteLock();

            return subscriber;
        }

        internal void removePublisher(int channelId)
        {
            lockObj.EnterWriteLock();
            publishers.Remove(channelId);
            lockObj.ExitWriteLock();
        }

        internal void removeSubscriber(int channelId)
        {
            lockObj.EnterWriteLock();
            subscribers.Remove(channelId);
            lockObj.ExitWriteLock();
        }

        private int getUnusedChannelId()
        {
            return nextChannelId++;
        }

        private bool dispatchReceivedMessage(string message)
        {
            string[] parts = message.Split(' ', 2);
            if (parts.Length != 2)
                return false;

            if (!int.TryParse(parts[0], out int channelId) || channelId <= 0)
                return false;

            Publisher? publisher = publishers.GetOrDefault(channelId, null);
            if (publisher != null)
                return publisher.handleMessage(parts[1]);

            Subscriber? subscriber = subscribers.GetOrDefault(channelId, null);
            if (subscriber != null)
                return subscriber.handleMessage(parts[1]);

            return channelId < nextChannelId;
        }

        internal bool sendMessage(int channelId, string message)
        {
            try
            {
                lockObj.EnterReadLock();
                if (closed || error)
                {
                    return false;
                }
            }
            finally
            {
                lockObj.ExitReadLock();
            }

            for (; ; )
            {
                try
                {
                    Log.Debug($"SEND CHID: {channelId}, MES: {message}");
                    outgoingMessageQueue.Add(channelId + " " + message + "\n");
                    break;
                }
                catch (ThreadAbortException)
                {
                    // empty
                }
            }
            return true;
        }
    }
}
