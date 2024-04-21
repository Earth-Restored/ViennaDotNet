using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.EventBus.Server
{
    public class NetworkServer
    {
        private readonly Server server;
        private readonly TcpListener serverSocket;

        public NetworkServer(Server server, int port)
        {
            this.server = server;
            serverSocket = new TcpListener(IPAddress.Loopback, port);
            serverSocket.Start();
            Log.Information($"Created server on port {port}");
        }

        public void run()
        {
            for (; ; )
            {
                try
                {
                    Socket socket = serverSocket.AcceptSocket();
                    Log.Information($"Connection from {socket.RemoteEndPoint}");
                    Connection connection = new Connection(this, socket);
                    new Thread(connection.run).Start();
                }
                catch (IOException exception)
                {
                    Log.Warning($"Exception while accepting connection: {exception}");
                }
            }
        }

        private sealed class Connection
        {
            private readonly NetworkServer networkServer;

            private readonly Socket socket;

            //private readonly NetworkStream outputStream;
            private readonly object sendLock = new object();

            private readonly Dictionary<int, Channel> channels = new Dictionary<int, Channel>();

            public Connection(NetworkServer networkServer, Socket socket)
            {
                this.networkServer = networkServer;
                this.socket = socket;
                //outputStream = new NetworkStream(this.socket);
            }

            public void run()
            {
                try
                {
                    byte[] readBuffer = new byte[1024];
                    MemoryStream byteArrayOutputStream = new MemoryStream(1024);
                    bool close = false;
                    while (!close)
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
                                    string command = Encoding.ASCII.GetString(byteArrayOutputStream.ToArray());

                                    Log.Debug($"REC MSG: {command}");

                                    if (!handleCommand(command))
                                    {
                                        close = true;
                                        break;
                                    }
                                    byteArrayOutputStream = new MemoryStream(1024);
                                    startOffset = offset + 1;
                                }
                            }

                            byteArrayOutputStream.Write(readBuffer, startOffset, readLength - startOffset);
                        }
                        else if (readLength == -1)
                            close = true;
                        else
                            throw new InvalidOperationException();
                    }
                }

                catch (IOException exception)
                {
                    Log.Warning("Exception while reading socket", exception);
                }
                handleClose();
            }

            internal void sendMessage(string message)
            {
                lock (sendLock)
                {
                    try
                    {
                        Log.Debug($"SEND MSG: {message}");
                        socket.Send(Encoding.ASCII.GetBytes(message + "\n"));
                    }
                    catch (IOException exception)
                    {
                        Log.Warning($"Exception while sending: {exception}");
                        try
                        {
                            socket.Close();
                        }
                        catch (IOException exception1)
                        {
                            Log.Warning($"Exception while closing socket: {exception1}");
                        }
                    }
                }
            }

            private bool handleCommand(string command)
            {
                string[] parts = command.Split(' ', 2);
                if (parts.Length != 2)
                    return false;

                if (!int.TryParse(parts[0], out int channelId) || channelId <= 0)
                    return false;

                Channel? channel = channels.GetOrDefault(channelId, null);
                if (channel != null)
                {
                    if (parts[1] == "CLOSE")
                    {
                        channel.handleClose();
                        channels.Remove(channelId);
                    }
                    else
                        channel.handleCommand(parts[1]);

                    return true;
                }
                else
                {
                    if (parts[1] == "CLOSE")
                        return true;
                    else
                    {
                        channel = handleChannelOpenCommand(channelId, parts[1]);
                        if (channel != null)
                        {
                            channels[channelId] = channel;
                            return true;
                        }
                        else
                            return false;
                    }
                }
            }

            private void handleClose()
            {
                Log.Information("Connection closed");

                channels.Values.ForEach(channel => channel.handleClose());
            }

            private Channel? handleChannelOpenCommand(int channelId, string command)
            {
                string[] parts = command.Split(' ');
                if (parts.Length < 1)
                    return null;

                switch (parts[0])
                {
                    case "PUB":
                        return new PublisherChannel(this, channelId, networkServer.server.addPublisher());
                    case "SUB":
                        {
                            if (parts.Length < 2)
                                return null;

                            SubscriberChannel subscriberChannel = new SubscriberChannel(networkServer, this, channelId, parts[1]);
                            if (!subscriberChannel.isValid())
                                return null;

                            return subscriberChannel;
                        }
                    default:
                        return null;
                }
            }
        }

        private abstract class Channel
        {
            private readonly Connection connection;
            private readonly int channelId;

            protected Channel(Connection connection, int channelId)
            {
                this.connection = connection;
                this.channelId = channelId;
            }

            public abstract void handleCommand(string command);
            public abstract void handleClose();

            protected void sendMessage(string message)
            {
                connection.sendMessage(channelId.ToString() + " " + message);
            }
        }

        private sealed class PublisherChannel : Channel
        {
            private readonly Server.Publisher publisher;
            private bool _error = false;

            public PublisherChannel(Connection connection, int channelId, Server.Publisher publisher)
                    : base(connection, channelId)
            {
                this.publisher = publisher;
            }

            public override void handleCommand(string command)
            {
                if (_error)
                {
                    sendMessage("ERR");
                    return;
                }

                string[] parts = command.Split(' ', 2);
                if (parts[0] == "SEND")
                {
                    string entryString = parts[1];
                    string[] fields = entryString.Split(':', 3);
                    if (fields.Length != 3)
                    {
                        error();
                        return;
                    }

                    long timestamp = U.CurrentTimeMillis();
                    string queueName = fields[0];
                    string type = fields[1];
                    string data = fields[2];
                    if (publisher.publish(queueName, timestamp, type, data))
                        sendMessage("ACK");
                    else
                        error();
                }
                else
                    error();
            }

            public override void handleClose()
            {
                publisher.remove();
            }

            private void error()
            {
                _error = true;
                sendMessage("ERR");
            }
        }

        private sealed class SubscriberChannel : Channel
        {
            private readonly NetworkServer netServer;
            private readonly Server.Subscriber subscriber;

            public SubscriberChannel(NetworkServer _netServer, Connection connection, int channelId, string queueName)
                    : base(connection, channelId)
            {
                netServer = _netServer;
                subscriber = netServer.server.addSubscriber(queueName, handleMessage)!;
            }

            public bool isValid()
            {
                return subscriber != null;
            }

            public override void handleCommand(string command)
            {
                // empty
            }

            public override void handleClose()
            {
                subscriber.remove();
            }

            private void handleMessage(Server.Subscriber.Message message)
            {
                if (message is Server.Subscriber.EntryMessage entryMessage)
                {
                    StringBuilder stringBuilder = new StringBuilder();
                    stringBuilder.Append(entryMessage.timestamp);
                    stringBuilder.Append(":");
                    stringBuilder.Append(entryMessage.type);
                    stringBuilder.Append(":");
                    stringBuilder.Append(entryMessage.data);
                    sendMessage(stringBuilder.ToString());
                }
                else if (message is Server.Subscriber.ErrorMessage)
                    sendMessage("ERR");
            }
        }
    }
}
