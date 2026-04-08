using Serilog;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace ViennaDotNet.EventBus.Client;

public sealed class EventBusClient : IAsyncDisposable
{
    public static async Task<EventBusClient> ConnectAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        string[] parts = connectionString.Split(':', 2);
        string host = parts[0];

        if (parts.Length <= 1 || !int.TryParse(parts[1], out int port))
        {
            port = 5532;
        }

        if (port <= 0 || port > 65535)
        {
            throw new ArgumentException($"Invalid port number out of range: {port}", nameof(connectionString));
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(host, port, cancellationToken);
        }
        catch (SocketException ex)
        {
            socket.Dispose();
            throw new ConnectException($"Could not connect socket: {ex.Message}", ex);
        }

        return new EventBusClient(socket);
    }

    public sealed class ConnectException : Exception
    {
        public ConnectException(string? message, Exception? innerException = null) : base(message, innerException) { }
    }

    private readonly Socket _socket;
    private readonly NetworkStream _networkStream;
    private readonly Channel<string> _outgoingChannel;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Task _sendTask;
    private readonly Task _receiveTask;

    private int _disposedState = StateActive;
    private const int StateActive = 0;
    private const int StateDisposing = 1;
    private int _nextChannelId = 1;

    private readonly ConcurrentDictionary<int, Publisher> _publishers = new();
    private readonly ConcurrentDictionary<int, Subscriber> _subscribers = new();
    private readonly ConcurrentDictionary<int, RequestSender> _requestSenders = new();
    private readonly ConcurrentDictionary<int, RequestHandler> _requestHandlers = new();

    private EventBusClient(Socket socket)
    {
        _socket = socket;
        _networkStream = new NetworkStream(_socket, ownsSocket: true);

        _outgoingChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true, // We only have one loop reading from this to send to the socket
            SingleWriter = false
        });

        _sendTask = HandleSendLoopAsync(_shutdownCts.Token);
        _receiveTask = HandleReceiveLoopAsync(_shutdownCts.Token);
    }

    private async Task HandleSendLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _outgoingChannel.Reader.ReadAllAsync(cancellationToken))
            {
                byte[] bytes = Encoding.ASCII.GetBytes(message);
                await _networkStream.WriteAsync(bytes, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            Log.Error(ex, "Socket error in Send Loop");
        }
        finally
        {
            _shutdownCts.Cancel();
        }
    }

    private async Task HandleReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var reader = PipeReader.Create(_networkStream);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ReadResult result = await reader.ReadAsync(cancellationToken);
                ReadOnlySequence<byte> buffer = result.Buffer;

                while (TryReadLine(ref buffer, out ReadOnlySequence<byte> line))
                {
                    string message = Encoding.ASCII.GetString(line);

                    if (!await DispatchReceivedMessage(message))
                    {
                        InitiateClose();
                        break;
                    }
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            ClearAndNotify(_subscribers, s => s.Error());
            ClearAndNotify(_requestHandlers, h => h.Error());
        }
        finally
        {
            await reader.CompleteAsync();
            InitiateClose();
        }
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        SequencePosition? position = buffer.PositionOf((byte)'\n');

        if (position is null)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposedState, StateDisposing) != StateActive)
        {
            return;
        }

        InitiateCloseInternal();

        try
        {
            await Task.WhenAll(_sendTask, _receiveTask).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during EventBusClient background task completion");
        }
        finally
        {
            _shutdownCts.Dispose();
            _networkStream.Dispose();

            NotifyAllCollectionsClosed();
        }
    }

    public void InitiateClose()
    {
        if (Interlocked.Exchange(ref _disposedState, StateDisposing) == StateActive)
        {
            InitiateCloseInternal();
        }
    }

    private void InitiateCloseInternal()
    {
        _outgoingChannel.Writer.TryComplete();

        if (!_shutdownCts.IsCancellationRequested)
        {
            _shutdownCts.Cancel();
        }

        try
        {
            if (_socket.Connected)
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
        }
        catch
        {
        }

        _networkStream.Close();
    }

    private void NotifyAllCollectionsClosed()
    {
        ClearAndNotify(_publishers, p => p.Closed());
        ClearAndNotify(_requestSenders, r => r.Closed());
        ClearAndNotify(_subscribers, s => s.Close());
        ClearAndNotify(_requestHandlers, h => h.Close());
    }

    public Publisher AddPublisher()
    {
        int channelId = Interlocked.Increment(ref _nextChannelId);
        var publisher = new Publisher(this, channelId);

        if (SendMessage(channelId, "PUB"))
        {
            _publishers.TryAdd(channelId, publisher);
        }
        else
        {
            publisher.Closed();
        }

        return publisher;
    }

    public Subscriber AddSubscriber(string queueName, Subscriber.ISubscriberListener listener)
    {
        int channelId = Interlocked.Increment(ref _nextChannelId);
        var subscriber = new Subscriber(this, channelId, queueName, listener);

        if (SendMessage(channelId, "SUB " + queueName))
        {
            _subscribers.TryAdd(channelId, subscriber);
        }
        else
        {
            subscriber.Error();
        }

        return subscriber;
    }

    public RequestSender AddRequestSender()
    {
        int channelId = Interlocked.Increment(ref _nextChannelId);
        var requestSender = new RequestSender(this, channelId);

        if (SendMessage(channelId, "REQ"))
        {
            _requestSenders.TryAdd(channelId, requestSender);
        }
        else
        {
            requestSender.Closed();
        }

        return requestSender;
    }

    public RequestHandler AddRequestHandler(string queueName, RequestHandler.IHandler handler)
    {
        int channelId = Interlocked.Increment(ref _nextChannelId);
        var requestHandler = new RequestHandler(this, channelId, queueName, handler);

        if (SendMessage(channelId, "HND " + queueName))
        {
            _requestHandlers.TryAdd(channelId, requestHandler);
        }
        else
        {
            requestHandler.Error();
        }

        return requestHandler;
    }

    internal void RemovePublisher(int channelId)
        => _publishers.TryRemove(channelId, out _);

    internal void RemoveSubscriber(int channelId)
        => _subscribers.TryRemove(channelId, out _);

    internal void RemoveRequestSender(int channelId)
        => _requestSenders.TryRemove(channelId, out _);

    internal void RemoveRequestHandler(int channelId)
        => _requestHandlers.TryRemove(channelId, out _);

    private async Task<bool> DispatchReceivedMessage(string message)
    {
        string[] parts = message.Split(' ', 2);
        if (parts.Length != 2 || !int.TryParse(parts[0], out int channelId) || channelId <= 0)
        {
            return false;
        }

        if (_publishers.TryGetValue(channelId, out var publisher) && await publisher.HandleMessage(parts[1]))
        {
            return true;
        }

        if (_subscribers.TryGetValue(channelId, out var subscriber) && await subscriber.HandleMessage(parts[1]))
        {
            return true;
        }

        if (_requestSenders.TryGetValue(channelId, out var sender) && await sender.HandleMessage(parts[1]))
        {
            return true;
        }

        if (_requestHandlers.TryGetValue(channelId, out var handler) && await handler.HandleMessage(parts[1]))
        {
            return true;
        }

        return channelId <= _nextChannelId;
    }

    internal bool SendMessage(int channelId, string message)
    {
        if (_disposedState is not StateActive)
        {
            return false;
        }

        return _outgoingChannel.Writer.TryWrite($"{channelId} {message}\n");
    }

    private static void ClearAndNotify<T>(ConcurrentDictionary<int, T> collection, Action<T> action)
    {
        foreach (var item in collection.Values)
        {
            action(item);
        }

        collection.Clear();
    }
}