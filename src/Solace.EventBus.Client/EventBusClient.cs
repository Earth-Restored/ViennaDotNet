using Serilog;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace Solace.EventBus.Client;

public sealed class EventBusClient : IAsyncDisposable
{
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _networkStream;

    private readonly Channel<string> _outgoingMessageQueue;
    private readonly Task _outgoingTask;
    private readonly Task _incomingTask;
    private readonly CancellationTokenSource _cts = new();

    private int _isClosed;
    private int _hasError;

    private readonly ConcurrentDictionary<int, Publisher> _publishers = new();
    private readonly ConcurrentDictionary<int, Subscriber> _subscribers = new();
    private readonly ConcurrentDictionary<int, RequestSender> _requestSenders = new();
    private readonly ConcurrentDictionary<int, RequestHandler> _requestHandlers = new();

    private int _nextChannelId = 1;

    private EventBusClient(TcpClient tcpClient)
    {
        _tcpClient = tcpClient;
        _networkStream = _tcpClient.GetStream();
        _outgoingMessageQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _outgoingTask = Task.Run(ProcessOutgoingMessagesAsync);
        _incomingTask = Task.Run(ProcessIncomingMessagesAsync);
    }

    public static async Task<EventBusClient> ConnectAsync(string connectionString)
    {
        string[] parts = connectionString.Split(':', 2);
        string host = parts[0];

        if (parts.Length <= 1 || !int.TryParse(parts[1], out int port))
        {
            port = 5532;
        }

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentException("Port number out of range");
        }

        var tcpClient = new TcpClient();
        try
        {
            await tcpClient.ConnectAsync(host, port);
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            tcpClient.Dispose();
            throw new ConnectException("Could not create socket", exception);
        }

        return new EventBusClient(tcpClient);
    }

    public sealed class ConnectException : Exception
    {
        public ConnectException(string message) : base(message) { }
        public ConnectException(string message, Exception innerException) : base(message, innerException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await InitiateCloseAsync();

        try
        {
            await Task.WhenAll(_incomingTask, _outgoingTask);
        }
        catch
        {
            // Suppress exceptions from cancelled background tasks
        }
    }

    private async Task InitiateCloseAsync()
    {
        if (Interlocked.Exchange(ref _isClosed, 1) == 0)
        {
            _cts.Cancel();
            _outgoingMessageQueue.Writer.TryComplete();

            try
            {
                _tcpClient.Dispose(); // Closes underlying stream and socket
            }
            catch
            {
                // empty
            }
        }

        await Task.CompletedTask;
    }

    private void SetError()
    {
        Interlocked.Exchange(ref _hasError, 1);
        _ = InitiateCloseAsync(); // Fire and forget closure on error
    }

    private async Task ProcessOutgoingMessagesAsync()
    {
        try
        {
            await foreach (var message in _outgoingMessageQueue.Reader.ReadAllAsync(_cts.Token))
            {
                byte[] buffer = Encoding.ASCII.GetBytes(message);
                await _networkStream.WriteAsync(buffer, _cts.Token);
                await _networkStream.FlushAsync(_cts.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
            SetError();
        }

        await InitiateCloseAsync();

        foreach (var kvp in _publishers)
        {
            await kvp.Value.ClosedAsync();
        }

        _publishers.Clear();

        foreach (var kvp in _requestSenders)
        {
            await kvp.Value.ClosedAsync();
        }

        _requestSenders.Clear();
    }

    private async Task ProcessIncomingMessagesAsync()
    {
        var reader = PipeReader.Create(_networkStream);

        try
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync(_cts.Token);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                {
                    string message = Encoding.ASCII.GetString(line.IsSingleSegment ? line.FirstSpan : line.ToArray());

                    bool suppress = Volatile.Read(ref _isClosed) == 1 || Volatile.Read(ref _hasError) == 1;
                    if (!suppress)
                    {
                        if (!await DispatchReceivedMessageAsync(message))
                        {
                            SetError();
                        }
                    }
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException)
        {
            Log.Error(ex, "EventBusClient error");
            SetError();
        }

        await reader.CompleteAsync();
        await InitiateCloseAsync();

        foreach (var kvp in _subscribers)
        {
            await kvp.Value.ErrorAsync();
        }

        _subscribers.Clear();

        foreach (var kvp in _requestHandlers)
        {
            await kvp.Value.ErrorAsync();
        }

        _requestHandlers.Clear();
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

    public async Task<Publisher> AddPublisherAsync()
    {
        int channelId = GetUnusedChannelId();
        var publisher = new Publisher(this, channelId);

        if (await SendMessageAsync(channelId, "PUB"))
        {
            _publishers.TryAdd(channelId, publisher);
        }
        else
        {
            await publisher.ClosedAsync();
        }

        return publisher;
    }

    public async Task<Subscriber> AddSubscriberAsync(string queueName, ISubscriberListener listener)
    {
        int channelId = GetUnusedChannelId();
        var subscriber = new Subscriber(this, channelId, queueName, listener);

        if (await SendMessageAsync(channelId, $"SUB {queueName}"))
        {
            _subscribers.TryAdd(channelId, subscriber);
        }
        else
        {
            await subscriber.ErrorAsync();
        }

        return subscriber;
    }

    public async Task<RequestSender> AddRequestSenderAsync()
    {
        int channelId = GetUnusedChannelId();
        var requestSender = new RequestSender(this, channelId);

        if (await SendMessageAsync(channelId, "REQ"))
        {
            _requestSenders.TryAdd(channelId, requestSender);
        }
        else
        {
            await requestSender.ClosedAsync();
        }

        return requestSender;
    }

    public async Task<RequestHandler> AddRequestHandlerAsync(string queueName, IRequestHandlerLister handler)
    {
        int channelId = GetUnusedChannelId();
        var requestHandler = new RequestHandler(this, channelId, queueName, handler);

        if (await SendMessageAsync(channelId, $"HND {queueName}"))
        {
            _requestHandlers.TryAdd(channelId, requestHandler);
        }
        else
        {
            await requestHandler.ErrorAsync();
        }

        return requestHandler;
    }

    internal void RemovePublisher(int channelId) => _publishers.TryRemove(channelId, out _);
    internal void RemoveSubscriber(int channelId) => _subscribers.TryRemove(channelId, out _);
    internal void RemoveRequestSender(int channelId) => _requestSenders.TryRemove(channelId, out _);
    internal void RemoveRequestHandler(int channelId) => _requestHandlers.TryRemove(channelId, out _);

    private int GetUnusedChannelId()
        => Interlocked.Increment(ref _nextChannelId) - 1;

    private async Task<bool> DispatchReceivedMessageAsync(string message)
    {
        string[] parts = message.Split(' ', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out int channelId) || channelId <= 0)
        {
            return false;
        }

        if (_publishers.TryGetValue(channelId, out var publisher))
        {
            return await publisher.HandleMessageAsync(parts[1]);
        }

        if (_subscribers.TryGetValue(channelId, out var subscriber))
        {
            return await subscriber.HandleMessageAsync(parts[1]);
        }

        if (_requestSenders.TryGetValue(channelId, out var requestSender))
        {
            return await requestSender.HandleMessageAsync(parts[1]);
        }

        if (_requestHandlers.TryGetValue(channelId, out var requestHandler))
        {
            return await requestHandler.HandleMessageAsync(parts[1]);
        }

        return channelId < Volatile.Read(ref _nextChannelId);
    }

    internal async ValueTask<bool> SendMessageAsync(int channelId, string message)
    {
        if (Volatile.Read(ref _isClosed) == 1 || Volatile.Read(ref _hasError) == 1)
        {
            return false;
        }

        try
        {
            await _outgoingMessageQueue.Writer.WriteAsync($"{channelId} {message}\n", _cts.Token);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}