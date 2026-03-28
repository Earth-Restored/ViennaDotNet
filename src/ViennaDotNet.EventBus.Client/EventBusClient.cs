using Serilog;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.EventBus.Client;

public sealed class EventBusClient : IDisposable
{
    public static EventBusClient Create(string connectionString)
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
        {
            throw new ArgumentException("Port number out of range", nameof(connectionString));
        }

        Socket socket;
        try
        {
            socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(host, port);
        }
        catch (SocketException ex)
        {
            throw new ConnectException($"Could not create socket: {ex}");
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

    private readonly Socket _socket;
    private readonly BlockingCollection<string> _outgoingMessageQueue = [];
    private readonly CancellationTokenSource _tokenSource = new();
    private readonly Task _outgoingThread;
    private readonly Task _incomingThread;
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

    private bool _closed = false;
    private bool _error = false;

    private readonly Dictionary<int, Publisher> _publishers = [];
    private readonly Dictionary<int, Subscriber> _subscribers = [];
    private readonly Dictionary<int, RequestSender> _requestSenders = [];
    private readonly Dictionary<int, RequestHandler> _requestHandlers = [];
    private int _nextChannelId = 1;

    private EventBusClient(Socket socket)
    {
        _socket = socket;

        _outgoingThread = Task.Factory.StartNew(() => HandleSendLoop(_tokenSource.Token), _tokenSource.Token/*, TaskCreationOptions.LongRunning, TaskScheduler.Default*/).Unwrap();

        _incomingThread = Task.Factory.StartNew(() => HandleReceiveLoop(_tokenSource.Token), _tokenSource.Token/*, TaskCreationOptions.LongRunning, TaskScheduler.Default*/).Unwrap();
    }

    private async Task HandleSendLoop(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var message in _outgoingMessageQueue.GetConsumingEnumerable(cancellationToken))
            {
                byte[] bytes = Encoding.ASCII.GetBytes(message);
                await _socket.SendAsync(bytes, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // empty
        }
        catch (SocketException)
        {
            _lock.EnterWriteLock();
            _error = true;
            _lock.ExitWriteLock();
        }

        InitiateClose();

        _publishers.ForEach((channelId, publisher) =>
        {
            publisher.Closed();
        });
        _publishers.Clear();
        _requestSenders.ForEach((channelId, requestSender) =>
        {
            requestSender.Closed();
        });
        _requestSenders.Clear();
    }

    private async Task HandleReceiveLoop(CancellationToken cancellationToken)
    {
        int sleepCounter = 0;
        try
        {
            byte[] readBuffer = new byte[1024];
            MemoryStream byteArrayOutputStream = new MemoryStream(1024);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int readLength = await _socket.ReceiveAsync(readBuffer, cancellationToken);
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
                            bool suppress = _closed || _error;
                            _lock.ExitReadLock();

                            if (!suppress)
                            {
                                if (!await DispatchReceivedMessage(message))
                                {
                                    _lock.EnterWriteLock();
                                    _error = true;
                                    _lock.ExitWriteLock();
                                    InitiateClose();
                                }
                            }

                            byteArrayOutputStream = new MemoryStream(1024);
                            startOffset = offset + 1;
                        }
                    }

                    byteArrayOutputStream.Write(readBuffer, startOffset, readLength - startOffset);
                }
                else if (readLength == 0)
                {
                    // because we are using async, Socket.Blocking isn't used and the Receive method returns even when it is connected and no data has been received
                    if (!_socket.Connected)
                    {
                        break;
                    }
                }
                else
                {
                    throw new InvalidOperationException();
                }

                // reduce CPU usage
                if (sleepCounter >= 2500)
                {
                    sleepCounter = 0;
                    await Task.Delay(1, cancellationToken);
                }
                else
                {
                    await Task.Yield();
                }

                sleepCounter++;
            }
        }
        catch (SocketException)
        {
            _lock.EnterWriteLock();
            _error = true;
            _lock.ExitWriteLock();
        }

        InitiateClose();

        _subscribers.ForEach((channelId, subscriber) =>
        {
            subscriber.Error();
        });
        _subscribers.Clear();

        _requestHandlers.ForEach((channelId, requestHandler) =>
        {
            requestHandler.Error();
        });
        _requestHandlers.Clear();
    }

    public void Close()
    {
        InitiateClose();

        try
        {
            _incomingThread.Wait();
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
        {
            // empty
        }
        catch (Exception ex)
        {
            Log.Error($"Exception in incoming thread: {ex}");
        }

        try
        {
            _outgoingThread.Wait();
        }
        catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
        {
            // empty
        }
        catch (Exception ex)
        {
            Log.Error($"Exception in outgoing thread: {ex}");
        }
    }

    public void Dispose()
        => Close();

    private void InitiateClose()
    {
        _lock.EnterWriteLock();
        if (!_error)
        {
            _closed = true;
        }

        _lock.ExitWriteLock();

        try
        {
            _socket.Shutdown(SocketShutdown.Both);
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
            _socket.Close();
        }

        _tokenSource.Cancel();
    }

    public Publisher AddPublisher()
    {
        _lock.EnterWriteLock();
        int channelId = GetUnusedChannelId();
        Publisher publisher = new Publisher(this, channelId);
        if (SendMessage(channelId, "PUB"))
        {
            _publishers[channelId] = publisher;
        }
        else
        {
            publisher.Closed();
        }

        _lock.ExitWriteLock();

        return publisher;
    }

    public Subscriber AddSubscriber(string queueName, Subscriber.ISubscriberListener listener)
    {
        _lock.EnterWriteLock();
        int channelId = GetUnusedChannelId();
        Subscriber subscriber = new Subscriber(this, channelId, queueName, listener);
        if (SendMessage(channelId, "SUB " + queueName))
        {
            _subscribers[channelId] = subscriber;
        }
        else
        {
            subscriber.Error();
        }

        _lock.ExitWriteLock();

        return subscriber;
    }

    public RequestSender AddRequestSender()
    {
        _lock.EnterWriteLock();
        int channelId = GetUnusedChannelId();
        RequestSender requestSender = new RequestSender(this, channelId);
        if (SendMessage(channelId, "REQ"))
        {
            _requestSenders[channelId] = requestSender;
        }
        else
        {
            requestSender.Closed();
        }

        _lock.ExitWriteLock();
        return requestSender;
    }

    public RequestHandler AddRequestHandler(string queueName, RequestHandler.IHandler handler)
    {
        _lock.EnterWriteLock();
        int channelId = GetUnusedChannelId();
        RequestHandler requestHandler = new RequestHandler(this, channelId, queueName, handler);
        if (SendMessage(channelId, "HND " + queueName))
        {
            _requestHandlers[channelId] = requestHandler;
        }
        else
        {
            requestHandler.Error();
        }

        _lock.ExitWriteLock();
        return requestHandler;
    }

    internal void RemovePublisher(int channelId)
    {
        _lock.EnterWriteLock();
        _publishers.Remove(channelId);
        _lock.ExitWriteLock();
    }

    internal void RemoveSubscriber(int channelId)
    {
        _lock.EnterWriteLock();
        _subscribers.Remove(channelId);
        _lock.ExitWriteLock();
    }

    internal void RemoveRequestSender(int channelId)
    {
        _lock.EnterWriteLock();
        _requestSenders.Remove(channelId);
        _lock.ExitWriteLock();
    }

    internal void RemoveRequestHandler(int channelId)
    {
        _lock.EnterWriteLock();
        _requestHandlers.Remove(channelId);
        _lock.ExitWriteLock();
    }

    private int GetUnusedChannelId()
        => _nextChannelId++;

    private async Task<bool> DispatchReceivedMessage(string message)
    {
        string[] parts = message.Split(' ', 2);
        if (parts.Length != 2)
            return false;

        if (!int.TryParse(parts[0], out int channelId) || channelId <= 0)
            return false;

        Publisher? publisher = _publishers.GetOrDefault(channelId, null);
        if (publisher is not null)
        {
            if (await publisher.HandleMessage(parts[1]))
            {
                return true;
            }
        }

        Subscriber? subscriber = _subscribers.GetOrDefault(channelId, null);
        if (subscriber is not null)
        {
            if (await subscriber.HandleMessage(parts[1]))
            {
                return true;
            }
        }

        RequestSender? requestSender = _requestSenders.GetOrDefault(channelId, null);
        if (requestSender is not null)
        {
            if (await requestSender.HandleMessage(parts[1]))
            {
                return true;
            }
        }

        RequestHandler? requestHandler = _requestHandlers.GetOrDefault(channelId, null);
        if (requestHandler is not null)
        {
            if (await requestHandler.HandleMessage(parts[1]))
            {
                return true;
            }
        }

        return channelId < _nextChannelId;
    }

    internal bool SendMessage(int channelId, string message)
    {
        try
        {
            _lock.EnterReadLock();
            if (_closed || _error)
            {
                return false;
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }

        while (true)
        {
            try
            {
                _outgoingMessageQueue.Add(channelId + " " + message + "\n");
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
