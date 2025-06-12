using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.EventBus.Client;

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
        catch (SocketException ex)
        {
            throw new ConnectException("Could not create socket", ex);
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
    private readonly BlockingCollection<string> outgoingMessageQueue = [];
    private Thread outgoingThread;
    private Thread incomingThread;
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    private bool closed = false;
    private bool error = false;

    private readonly Dictionary<int, Publisher> publishers = [];
    private readonly Dictionary<int, Subscriber> subscribers = [];
    private readonly Dictionary<int, RequestSender> requestSenders = [];
    private readonly Dictionary<int, RequestHandler> requestHandlers = [];
    private int nextChannelId = 1;

    private EventBusClient(Socket socket)
    {
        this.socket = socket;

        outgoingThread = new Thread(() =>
        {
            int sleepCounter = 0;
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

                    // reduce CPU usage
                    if (sleepCounter >= 2500)
                    {
                        sleepCounter = 0;
                        Thread.Sleep(1);
                    }
                    else
                        Thread.Sleep(0);
                    sleepCounter++;
                }
            }
            catch (ThreadInterruptedException)
            {
                // empty
            }
            catch (SocketException)
            {
                _lock.EnterWriteLock();
                error = true;
                _lock.ExitWriteLock();
            }

            initiateClose();

            publishers.ForEach((channelId, publisher) =>
            {
                publisher.closed();
            });
            publishers.Clear();
            requestSenders.ForEach((channelId, requestSender) =>
            {
                requestSender.closed();
            });
            requestSenders.Clear();
        });

        incomingThread = new Thread(() =>
        {
            int sleepCounter = 0;
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

                                _lock.EnterReadLock();
                                bool suppress = closed || error;
                                _lock.ExitReadLock();

                                if (!suppress)
                                {
                                    if (!dispatchReceivedMessage(message))
                                    {
                                        _lock.EnterWriteLock();
                                        error = true;
                                        _lock.ExitWriteLock();
                                        initiateClose();
                                    }
                                }

                                byteArrayOutputStream = new MemoryStream(1024);
                                startOffset = offset + 1;
                            }
                        }

                        byteArrayOutputStream.Write(readBuffer, startOffset, readLength - startOffset);
                    }
                    else if (readLength == 0)
                        break;
                    else
                        throw new InvalidOperationException();

                    // reduce CPU usage
                    if (sleepCounter >= 2500)
                    {
                        sleepCounter = 0;
                        Thread.Sleep(1);
                    }
                    else
                        Thread.Sleep(0);
                    sleepCounter++;
                }
            }
            catch (SocketException)
            {
                _lock.EnterWriteLock();
                error = true;
                _lock.ExitWriteLock();
            }

            initiateClose();

            subscribers.ForEach((channelId, subscriber) =>
            {
                subscriber.error();
            });
            subscribers.Clear();

            requestHandlers.ForEach((channelId, requestHandler) =>
            {
                requestHandler.error();
            });
            requestHandlers.Clear();
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
            catch (ThreadInterruptedException)
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
            catch (ThreadInterruptedException)
            {
                // empty
            }
        }
    }

    private void initiateClose()
    {
        _lock.EnterWriteLock();
        if (!error)
            closed = true;

        _lock.ExitWriteLock();

        try
        {
            socket.Shutdown(SocketShutdown.Both);
        }
        catch (SocketException)
        {
            // empty
        }
        catch (ObjectDisposedException)
        {
            // empty
        }
        finally
        {
            socket.Close();
        }

        outgoingThread.Interrupt();
    }

    public Publisher addPublisher()
    {
        _lock.EnterWriteLock();
        int channelId = getUnusedChannelId();
        Publisher publisher = new Publisher(this, channelId);
        if (sendMessage(channelId, "PUB"))
            publishers[channelId] = publisher;
        else
            publisher.closed();

        _lock.ExitWriteLock();

        return publisher;
    }

    public Subscriber addSubscriber(string queueName, Subscriber.ISubscriberListener listener)
    {
        _lock.EnterWriteLock();
        int channelId = getUnusedChannelId();
        Subscriber subscriber = new Subscriber(this, channelId, queueName, listener);
        if (sendMessage(channelId, "SUB " + queueName))
            subscribers[channelId] = subscriber;
        else
            subscriber.error();

        _lock.ExitWriteLock();

        return subscriber;
    }

    public RequestSender addRequestSender()
    {
        _lock.EnterWriteLock();
        int channelId = getUnusedChannelId();
        RequestSender requestSender = new RequestSender(this, channelId);
        if (sendMessage(channelId, "REQ"))
            requestSenders[channelId] = requestSender;
        else
            requestSender.closed();

        _lock.ExitWriteLock();
        return requestSender;
    }

    public RequestHandler addRequestHandler(string queueName, RequestHandler.IHandler handler)
    {
        _lock.EnterWriteLock();
        int channelId = getUnusedChannelId();
        RequestHandler requestHandler = new RequestHandler(this, channelId, queueName, handler);
        if (sendMessage(channelId, "HND " + queueName))
            requestHandlers[channelId] = requestHandler;
        else
            requestHandler.error();

        _lock.ExitWriteLock();
        return requestHandler;
    }

    internal void removePublisher(int channelId)
    {
        _lock.EnterWriteLock();
        publishers.Remove(channelId);
        _lock.ExitWriteLock();
    }

    internal void removeSubscriber(int channelId)
    {
        _lock.EnterWriteLock();
        subscribers.Remove(channelId);
        _lock.ExitWriteLock();
    }

    internal void removeRequestSender(int channelId)
    {
        _lock.EnterWriteLock();
        requestSenders.Remove(channelId);
        _lock.ExitWriteLock();
    }

    internal void removeRequestHandler(int channelId)
    {
        _lock.EnterWriteLock();
        requestHandlers.Remove(channelId);
        _lock.ExitWriteLock();
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

        RequestSender? requestSender = requestSenders.GetOrDefault(channelId, null);
        if (requestSender != null)
            return requestSender.handleMessage(parts[1]);

        RequestHandler? requestHandler = requestHandlers.GetOrDefault(channelId, null);
        if (requestHandler != null)
            return requestHandler.handleMessage(parts[1]);

        return channelId < nextChannelId;
    }

    internal bool sendMessage(int channelId, string message)
    {
        try
        {
            _lock.EnterReadLock();
            if (closed || error)
            {
                return false;
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        for (; ; )
        {
            try
            {
                outgoingMessageQueue.Add(channelId + " " + message + "\n");
                break;
            }
            catch (ThreadInterruptedException)
            {
                // empty
            }
        }

        return true;
    }
}
