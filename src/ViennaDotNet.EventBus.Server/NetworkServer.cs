using Serilog;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.EventBus.Server;

public sealed class NetworkServer
{
    private readonly Server _server;
    private readonly TcpListener _serverSocket;
    private readonly CancellationTokenSource _cts = new();

    public NetworkServer(Server server, int port)
    {
        _server = server;
        _serverSocket = new TcpListener(IPAddress.Loopback, port);
    }

    public async Task RunAsync()
    {
        _serverSocket.Start();
        Log.Information($"Created server on port {((IPEndPoint)_serverSocket.LocalEndpoint).Port}");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                Socket socket = await _serverSocket.AcceptSocketAsync(_cts.Token);
                Log.Information($"Connection from {socket.RemoteEndPoint}");

                var connection = new Connection(this, socket, _server);
                _ = connection.RunAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Server shutting down
        }
        catch (SocketException ex)
        {
            Log.Warning($"Exception while accepting connection: {ex}");
        }
    }

    public void Stop()
        => _cts.Cancel();

    private sealed class Connection
    {
        private readonly NetworkServer _networkServer;
        private readonly Socket _socket;
        private readonly Server _server;
        private readonly Channel<string> _sendChannel;
        private readonly Dictionary<int, ChannelHandler> _channels = [];

        public Connection(NetworkServer networkServer, Socket socket, Server server)
        {
            _networkServer = networkServer;
            _socket = socket;
            _server = server;

            _sendChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            {
                SingleReader = true, // Only the SendLoop reads
                SingleWriter = false
            });
        }

        public async Task RunAsync()
        {
            var stream = new NetworkStream(_socket, ownsSocket: true);
            var reader = PipeReader.Create(stream);
            var writer = PipeWriter.Create(stream);

            Task sendTask = SendLoopAsync(writer);

            try
            {
                while (true)
                {
                    ReadResult result = await reader.ReadAsync();
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                    {
                        string command = Encoding.ASCII.GetString(line);
                        if (!HandleCommand(command))
                        {
                            return; // graceful exit
                        }
                    }

                    reader.AdvanceTo(buffer.Start, buffer.End);

                    if (result.IsCompleted) break;
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Connection error: {ex.Message}");
            }
            finally
            {
                HandleClose();
                await reader.CompleteAsync();
                _sendChannel.Writer.Complete();
                await sendTask;
            }
        }

        private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
        {
            SequencePosition? position = buffer.PositionOf((byte)'\n');
            if (position == null)
            {
                line = default;
                return false;
            }

            line = buffer.Slice(0, position.Value);
            buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
            return true;
        }

        private async Task SendLoopAsync(PipeWriter writer)
        {
            try
            {
                await foreach (var message in _sendChannel.Reader.ReadAllAsync())
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(message + "\n");
                    await writer.WriteAsync(bytes);
                }
                await writer.CompleteAsync();
            }
            catch (Exception ex)
            {
                Log.Warning($"SendLoop exception: {ex.Message}");
            }
        }

        internal void SendMessage(string message)
            => _sendChannel.Writer.TryWrite(message);

        private bool HandleCommand(string command)
        {
            string[] parts = command.Split(' ', 2);
            if (parts.Length is not 2)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out int channelId) || channelId <= 0)
            {
                return false;
            }

            ChannelHandler? channel = _channels.GetOrDefault(channelId, null);
            if (channel is not null)
            {
                if (parts[1] is "CLOSE")
                {
                    channel.HandleClose();
                    _channels.Remove(channelId);
                }
                else
                {
                    channel.HandleCommand(parts[1]);
                }

                return true;
            }
            else
            {
                if (parts[1] is "CLOSE")
                {
                    return true;
                }
                else
                {
                    channel = HandleChannelOpenCommand(channelId, parts[1]);
                    if (channel is not null)
                    {
                        _channels[channelId] = channel;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
        }

        private void HandleClose()
        {
            Log.Information("Connection closed");

            foreach (var channel in _channels)
            {
                channel.Value.HandleClose();
            }
        }

        private ChannelHandler? HandleChannelOpenCommand(int channelId, string command)
        {
            string[] parts = command.Split(' ');
            if (parts.Length < 1)
            {
                return null;
            }

            switch (parts[0])
            {
                case "PUB":
                    var publisherChannel = new PublisherChannel(this, channelId, _networkServer);
                    if (!publisherChannel.IsValid)
                    {
                        return null;
                    }

                    return publisherChannel;
                case "SUB":
                    {
                        if (parts.Length < 2)
                        {
                            return null;
                        }

                        var subscriberChannel = new SubscriberChannel(_networkServer, this, channelId, parts[1]);
                        return !subscriberChannel.IsValid
                            ? null
                            : subscriberChannel;
                    }
                case "REQ":
                    {
                        var requestSenderChannel = new RequestSenderChannel(this, channelId, _networkServer);
                        return !requestSenderChannel.IsValid
                            ? null
                            : requestSenderChannel;
                    }
                case "HND":
                    {
                        if (parts.Length < 2)
                        {
                            return null;
                        }

                        var requestHandlerChannel = new RequestHandlerChannel(this, channelId, parts[1], _networkServer);
                        return !requestHandlerChannel.IsValid
                            ? null
                            : requestHandlerChannel;
                    }
                default:
                    return null;
            }
        }
    }

    private abstract class ChannelHandler
    {
        private readonly Connection _connection;
        private readonly int _channelId;

        protected ChannelHandler(Connection connection, int channelId)
        {
            _connection = connection;
            _channelId = channelId;
        }

        public abstract bool IsValid { get; }

        public abstract void HandleCommand(string command);
        public abstract void HandleClose();

        protected void SendMessage(string message)
            => _connection.SendMessage($"{_channelId} {message}");
    }

    private sealed class PublisherChannel : ChannelHandler
    {
        private readonly Server.Publisher _publisher;
        private bool _error = false;

        public PublisherChannel(Connection connection, int channelId, NetworkServer networkServer)
            : base(connection, channelId)
        {
            _publisher = networkServer._server.AddPublisher();
        }

        public override bool IsValid => true;

        public override void HandleCommand(string command)
        {
            if (_error)
            {
                SendMessage("ERR");
                return;
            }

            string[] parts = command.Split(' ', 2);
            if (parts[0] is "SEND")
            {
                string entryString = parts[1];
                string[] fields = entryString.Split(':', 3);
                if (fields.Length != 3)
                {
                    Error();
                    return;
                }

                long timestamp = U.CurrentTimeMillis();
                string queueName = fields[0];
                string type = fields[1];
                string data = fields[2];
                if (_publisher.Publish(queueName, timestamp, type, data))
                {
                    SendMessage("ACK");
                }
                else
                {
                    Error();
                }
            }
            else
            {
                Error();
            }
        }

        public override void HandleClose()
            => _publisher.Remove();

        private void Error()
        {
            _error = true;
            SendMessage("ERR");
        }
    }

    private sealed class SubscriberChannel : ChannelHandler
    {
        private readonly NetworkServer _netServer;
        private readonly Server.Subscriber? _subscriber;

        public SubscriberChannel(NetworkServer _netServer, Connection connection, int channelId, string queueName)
                : base(connection, channelId)
        {
            this._netServer = _netServer;
            _subscriber = this._netServer._server.AddSubscriber(queueName, HandleMessage);
        }

        public override bool IsValid => _subscriber is not null;

        public override void HandleCommand(string command)
        {
            // empty
        }

        public override void HandleClose()
            => _subscriber?.Remove();

        private void HandleMessage(Server.Subscriber.Message message)
        {
            if (message is Server.Subscriber.EntryMessage entryMessage)
            {
                var stringBuilder = new StringBuilder();
                stringBuilder.Append(entryMessage.Timestamp);
                stringBuilder.Append(':');
                stringBuilder.Append(entryMessage.Type);
                stringBuilder.Append(':');
                stringBuilder.Append(entryMessage.Data);
                SendMessage(stringBuilder.ToString());
            }
            else if (message is Server.Subscriber.ErrorMessage)
            {
                SendMessage("ERR");
            }
        }
    }

    private sealed class RequestSenderChannel : ChannelHandler
    {
        private readonly Server.RequestSender _requestSender;
        // TODO: should they be volatile?
        private volatile Task<string?>? _currentPendingResponse = null;
        private volatile bool _error = false;

        public RequestSenderChannel(Connection connection, int channelId, NetworkServer networkServer)
            : base(connection, channelId)
        {
            _requestSender = networkServer._server.AddRequestSender();
        }

        public override bool IsValid => true;

        public override void HandleCommand(string command)
        {
            if (_error)
            {
                SendMessage("ERR");
                return;
            }

            if (_currentPendingResponse is not null)
            {
                Error();
                return;
            }

            string[] parts = command.Split(' ', 2);
            if (parts[0] == "REQ")
            {
                string entryString = parts[1];
                string[] fields = entryString.Split(':', 3);
                if (fields.Length != 3)
                {
                    Error();
                    return;
                }

                long timestamp = U.CurrentTimeMillis();
                string queueName = fields[0];
                string type = fields[1];
                string data = fields[2];

                Task<string?>? completableFuture = _requestSender.RequestAsync(queueName, timestamp, type, data);
                if (completableFuture is not null)
                {
                    _currentPendingResponse = completableFuture;
                    SendMessage("ACK");
                    completableFuture.ContinueWith(task =>
                    {
                        if (_currentPendingResponse is not null)
                        {
                            if (_currentPendingResponse != completableFuture)
                            {
                                throw new InvalidOperationException();
                            }

                            _currentPendingResponse = null;
                            if (task.Result is not null)
                            {
                                SendMessage("REP " + task.Result);
                            }
                            else
                            {
                                SendMessage("NREP");
                            }
                        }
                    });
                }
                else
                {
                    Error();
                }
            }
            else
            {
                Error();
            }
        }

        public override void HandleClose()
        {
            _requestSender.Remove();
            _currentPendingResponse = null;
        }

        private void Error()
        {
            _error = true;
            _currentPendingResponse = null;
            SendMessage("ERR");
        }
    }

    private sealed class RequestHandlerChannel : ChannelHandler
    {
        private readonly Server.RequestHandler _requestHandler;
        private readonly Dictionary<int, TaskCompletionSource<string?>> _pendingResponses = [];
        private int _nextRequestId = 1;
        private bool _error = false;

        public RequestHandlerChannel(Connection connection, int channelId, string queueName, NetworkServer networkServer)
            : base(connection, channelId)
        {
            _requestHandler = networkServer._server.AddRequestHandler(queueName, HandleRequest, HandleError) ?? throw new ArgumentException($"{nameof(queueName)} is invalid.", nameof(queueName));
        }

        public override bool IsValid => _requestHandler is not null;

        public override void HandleCommand(string command)
        {
            if (_error)
            {
                SendMessage("ERR");
                return;
            }

            string[] parts = command.Split(' ', 2);
            if (parts[0] is "REP")
            {
                string entryString = parts[1];
                string[] fields = entryString.Split(':', 2);
                if (fields.Length != 2)
                {
                    Error();
                    return;
                }

                int requestId;
                try
                {
                    requestId = int.Parse(fields[0]);
                }
                catch (FormatException)
                {
                    Error();
                    return;
                }

                string data = fields[1];

                if (_pendingResponses.TryGetValue(requestId, out TaskCompletionSource<string?>? responseCompletableFuture))
                {
                    responseCompletableFuture.SetResult(data);
                }
                else
                {
                    Error();
                }

                _pendingResponses.Remove(requestId);
            }
            else if (parts[0] is "NREP")
            {
                int requestId;
                try
                {
                    requestId = int.Parse(parts[1]);
                }
                catch (FormatException)
                {
                    Error();
                    return;
                }

                TaskCompletionSource<string?>? responseCompletableFuture = _pendingResponses.JavaRemove(requestId);
                if (responseCompletableFuture is not null)
                {
                    responseCompletableFuture.SetResult(null);
                }
                else
                {
                    Error();
                }
            }
            else
            {
                Error();
            }
        }

        public override void HandleClose()
        {
            _requestHandler.Remove();
            foreach (var source in _pendingResponses.Values)
            {
                source.SetResult(null);
            }

            _pendingResponses.Clear();
        }

        private TaskCompletionSource<string?> HandleRequest(Server.RequestHandler.RequestR request)
        {
            int requestId = _nextRequestId++;
            TaskCompletionSource<string?> responseCompletableFuture = new();
            _pendingResponses[requestId] = responseCompletableFuture;

            var stringBuilder = new StringBuilder();
            stringBuilder.Append(requestId);
            stringBuilder.Append(':');
            stringBuilder.Append(request.Timestamp);
            stringBuilder.Append(':');
            stringBuilder.Append(request.Type);
            stringBuilder.Append(':');
            stringBuilder.Append(request.Data);
            SendMessage(stringBuilder.ToString());

            return responseCompletableFuture;
        }

        private void HandleError(Server.RequestHandler.ErrorMessage errorMessage)
            => Error();

        private void Error()
        {
            _error = true;
            foreach (var item in _pendingResponses)
            {
                item.Value.SetResult(null);
            }

            _pendingResponses.Clear();
            SendMessage("ERR");
        }
    }
}
