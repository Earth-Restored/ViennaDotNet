using Serilog;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
                catch (SocketException ex)
                {
                    Log.Warning($"Exception while accepting connection: {ex}");
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
                        else if (readLength == 0)
                            close = true;
                        else
                            throw new InvalidOperationException();
                    }
                }
                catch (SocketException ex)
                {
                    Log.Warning($"Exception while reading socket: {ex}");
                }
                handleClose();
            }

            internal void sendMessage(string message)
            {
                lock (sendLock)
                {
                    try
                    {
                        socket.Send(Encoding.ASCII.GetBytes(message + "\n"));
                    }
                    catch (SocketException ex)
                    {
                        Log.Warning($"Exception while sending: {ex}");
                        try
                        {
                            socket.Shutdown(SocketShutdown.Both);
                        }
                        catch (SocketException shutdownEx)
                        {
                            Log.Warning($"Exception while shutting down socket: {shutdownEx}");
                        }
                        finally
                        {
                            socket.Close();
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
                        PublisherChannel publisherChannel = new PublisherChannel(this, channelId, networkServer);
                        if (!publisherChannel.isValid())
                            return null;

                        return publisherChannel;
                    case "SUB":
                        {
                            if (parts.Length < 2)
                                return null;

                            SubscriberChannel subscriberChannel = new SubscriberChannel(networkServer, this, channelId, parts[1]);
                            if (!subscriberChannel.isValid())
                                return null;

                            return subscriberChannel;
                        }
                    case "REQ":
                        {
                            RequestSenderChannel requestSenderChannel = new RequestSenderChannel(this, channelId, networkServer);
                            if (!requestSenderChannel.isValid())
                                return null;

                            return requestSenderChannel;
                        }
                    case "HND":
                        {
                            if (parts.Length < 2)
                                return null;

                            RequestHandlerChannel requestHandlerChannel = new RequestHandlerChannel(this, channelId, parts[1], networkServer);
                            if (!requestHandlerChannel.isValid())
                                return null;

                            return requestHandlerChannel;
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

            public abstract bool isValid();

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

            public PublisherChannel(Connection connection, int channelId, NetworkServer networkServer)
                    : base(connection, channelId)
            {
                publisher = networkServer.server.addPublisher();
            }

            public override bool isValid()
                => true;

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

            public override bool isValid()
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

        private sealed class RequestSenderChannel : Channel
        {
            private readonly Server.RequestSender requestSender;
            // TODO: should they be volatile?
            private volatile TaskCompletionSource<string>? currentPendingResponse = null;
            private volatile bool _error = false;

            public RequestSenderChannel(Connection connection, int channelId, NetworkServer networkServer)
                : base(connection, channelId)
            {
                requestSender = networkServer.server.addRequestSender();
            }

            public override bool isValid()
            {
                return true;
            }

            public override void handleCommand(string command)
            {
                if (_error)
                {
                    sendMessage("ERR");
                    return;
                }

                if (currentPendingResponse != null)
                {
                    error();
                    return;
                }

                string[] parts = command.Split(' ', 2);
                if (parts[0] == "REQ")
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

                    TaskCompletionSource<string> completableFuture = requestSender.request(queueName, timestamp, type, data);
                    if (completableFuture != null)
                    {
                        currentPendingResponse = completableFuture;
                        sendMessage("ACK");
                        completableFuture.Task.ContinueWith(task =>
                        {
                            if (currentPendingResponse != null)
                            {
                                if (currentPendingResponse != completableFuture)
                                    throw new InvalidOperationException();

                                currentPendingResponse = null;
                                if (task.Result != null)
                                    sendMessage("REP " + task.Result);
                                else
                                    sendMessage("NREP");
                            }
                        });
                    }
                    else
                        error();
                }
                else
                    error();
            }

            public override void handleClose()
            {
                requestSender.remove();
                currentPendingResponse = null;
            }

            private void error()
            {
                _error = true;
                currentPendingResponse = null;
                sendMessage("ERR");
            }
        }

        private sealed class RequestHandlerChannel : Channel
        {
            private readonly Server.RequestHandler requestHandler;
            private readonly Dictionary<int, TaskCompletionSource<string?>> pendingResponses = new();
            private int nextRequestId = 1;
            private bool _error = false;

            public RequestHandlerChannel(Connection connection, int channelId, string queueName, NetworkServer networkServer)
                : base(connection, channelId)
            {
                requestHandler = networkServer.server.addRequestHandler(queueName, this.handleRequest, this.handleError);
            }

            public override bool isValid()
            {
                return requestHandler != null;
            }

            public override void handleCommand(string command)
            {
                if (_error)
                {
                    sendMessage("ERR");
                    return;
                }

                string[] parts = command.Split(' ', 2);
                if (parts[0] == "REP")
                {
                    string entryString = parts[1];
                    string[] fields = entryString.Split(':', 2);
                    if (fields.Length != 2)
                    {
                        error();
                        return;
                    }

                    int requestId;
                    try
                    {
                        requestId = int.Parse(fields[0]);
                    }
                    catch (FormatException)
                    {
                        error();
                        return;
                    }
                    string data = fields[1];

                    if (pendingResponses.TryGetValue(requestId, out TaskCompletionSource<string?>? responseCompletableFuture))
                        responseCompletableFuture.SetResult(data);
                    else
                        error();

                    pendingResponses.Remove(requestId);
                }
                else if (parts[0] == "NREP")
                {
                    int requestId;
                    try
                    {
                        requestId = int.Parse(parts[1]);
                    }
                    catch (FormatException)
                    {
                        error();
                        return;
                    }

                    TaskCompletionSource<string?>? responseCompletableFuture = pendingResponses.JavaRemove(requestId);
                    if (responseCompletableFuture != null)
                        responseCompletableFuture.SetResult(null);
                    else
                        error();
                }
                else
                    error();
            }

            public override void handleClose()
            {
                requestHandler.remove();
                pendingResponses.Values.ForEach(completableFuture => completableFuture.SetResult(null));
                pendingResponses.Clear();
            }

            private TaskCompletionSource<string?> handleRequest(Server.RequestHandler.Request request)
            {
                int requestId = nextRequestId++;
                TaskCompletionSource<string?> responseCompletableFuture = new();
                pendingResponses[requestId] = responseCompletableFuture;

                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.Append(requestId);
                stringBuilder.Append(":");
                stringBuilder.Append(request.timestamp.ToString());
                stringBuilder.Append(":");
                stringBuilder.Append(request.type);
                stringBuilder.Append(":");
                stringBuilder.Append(request.data);
                sendMessage(stringBuilder.ToString());

                return responseCompletableFuture;
            }

            private void handleError(Server.RequestHandler.ErrorMessage errorMessage)
            {
                error();
            }

            private void error()
            {
                _error = true;
                pendingResponses.Values.ForEach(completableFuture => completableFuture.SetResult(null));
                pendingResponses.Clear();
                sendMessage("ERR");
            }
        }
    }
}
