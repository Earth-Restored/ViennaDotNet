using Serilog;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.EventBus.Server;

public partial class Server
{
    private readonly ReaderWriterLockSlim _subscribersLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    private readonly Dictionary<string, HashSet<Subscriber>> _subscribers = [];

    private readonly ReaderWriterLockSlim _requestHandlersLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
    private readonly Dictionary<string, HashSet<RequestHandler>> _requestHandlers = [];

    public Subscriber? AddSubscriber(string queueName, Action<Subscriber.Message> consumer)
    {
        if (!ValidateQueueName(queueName))
        {
            return null;
        }

        Log.Debug($"Adding subscriber for {queueName}");

        _subscribersLock.EnterWriteLock();

        var subscriber = new Subscriber(this, queueName, consumer);
        _subscribers.ComputeIfAbsent(queueName, name => [])!.Add(subscriber);

        _subscribersLock.ExitWriteLock();

        return subscriber;
    }

    public sealed class Subscriber
    {
        private readonly Server _server;

        private readonly string _queueName;
        private readonly Action<Message> _consumer;
        private bool _ended = false;

        internal Subscriber(Server server, string queueName, Action<Message> consumer)
        {
            _server = server;
            _queueName = queueName;
            _consumer = consumer;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Remove()
        {
            _ended = true;

            Task.Run(() =>
            {
                Log.Debug("Removing subscriber");
                _server._subscribersLock.EnterWriteLock();
                try
                {
                    if (_server._subscribers.TryGetValue(_queueName, out var subs))
                    {
                        subs.Remove(this);
                    }
                }
                finally
                {
                    _server._subscribersLock.ExitWriteLock();
                }
            });
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal void Push(EntryMessage entryMessage)
        {
            if (!_ended)
            {
                _consumer.Invoke(entryMessage);
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal void Error()
        {
            if (!_ended)
            {
                _consumer.Invoke(new ErrorMessage());
                _ended = true;
            }
        }

        public abstract class Message
        {
            protected Message()
            {
                // empty
            }
        }

        public sealed class EntryMessage : Message
        {
            public readonly long Timestamp;
            public readonly string Type;
            public readonly string Data;

            internal EntryMessage(long timestamp, string type, string data)
            {
                Timestamp = timestamp;
                Type = type;
                Data = data;
            }
        }

        public sealed class ErrorMessage : Message
        {
            internal ErrorMessage()
            {
                // empty
            }
        }
    }

    private HashSet<Subscriber> GetSubscribers(string queueName)
    {
        HashSet<Subscriber>? subscribers = _subscribers.GetOrDefault(queueName, null);
        return subscribers is not null
            ? subscribers
            : [];
    }

    public Publisher AddPublisher()
    {
        Log.Debug("Adding publisher");
        return new Publisher(this);
    }

    public sealed class Publisher
    {
        private readonly Server _server;
        private bool _closed = false;

        public Publisher(Server server)
        {
            _server = server;
        }

        public void Remove()
        {
            Log.Debug("Removing publisher");
            _closed = true;
        }

        public bool Publish(string queueName, long timestamp, string type, string data)
        {
            if (_closed)
            {
                throw new Exception();
            }

            if (!ValidateQueueName(queueName))
            {
                return false;
            }

            if (!ValidateType(type))
            {
                return false;
            }

            if (!ValidateData(data))
            {
                return false;
            }

            _server._subscribersLock.EnterReadLock();

            var message = new Subscriber.EntryMessage(timestamp, type, data);
            foreach (var subscriber in _server.GetSubscribers(queueName))
            {
                subscriber.Push(message);
            }

            _server._subscribersLock.ExitReadLock();

            return true;
        }
    }

    public Server.RequestHandler? AddRequestHandler(string queueName, Func<RequestHandler.RequestR, TaskCompletionSource<string?>> requestHandler, Action<RequestHandler.ErrorMessage> errorConsumer)
    {
        if (!ValidateQueueName(queueName))
        {
            return null;
        }

        Log.Debug($"Adding request handler for {queueName}");

        _requestHandlersLock.EnterWriteLock();

        var handler = new RequestHandler(this, queueName, requestHandler, errorConsumer);
        _requestHandlers.ComputeIfAbsent(queueName, name => [])!.Add(handler);

        _requestHandlersLock.ExitWriteLock();

        return handler;
    }

    public sealed class RequestHandler
    {
        private readonly Server _server;

        private readonly string _queueName;
        private readonly Func<RequestR, TaskCompletionSource<string?>> _requestHandler;
        private readonly Action<ErrorMessage> _errorConsumer;
        private bool _ended = false;

        internal RequestHandler(Server server, string queueName, Func<RequestR, TaskCompletionSource<string?>> requestHandler, Action<ErrorMessage> errorConsumer)
        {
            _server = server;
            _queueName = queueName;
            _requestHandler = requestHandler;
            _errorConsumer = errorConsumer;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void Remove()
        {
            _ended = true;

            Task.Run(() =>
            {
                Log.Debug("Removing handler");
                _server._requestHandlersLock.EnterWriteLock();
                try
                {
                    if (_server._requestHandlers.TryGetValue(_queueName, out var handlers))
                    {
                        handlers.Remove(this);
                    }
                }
                finally
                {
                    _server._requestHandlersLock.ExitWriteLock();
                }
            });
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal TaskCompletionSource<string?> Request(RequestR request)
        {
            if (!_ended)
            {
                return _requestHandler.Invoke(request);
            }
            else
            {
                var source = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                source.SetResult(null);
                return source;
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void Error()
        {
            if (!_ended)
            {
                _errorConsumer.Invoke(new ErrorMessage());
                _ended = true;
            }
        }

        public sealed class RequestR
        {
            public readonly long Timestamp;
            public readonly string Type;
            public readonly string Data;

            internal RequestR(long timestamp, string type, string data)
            {
                Timestamp = timestamp;
                Type = type;
                Data = data;
            }
        }

        public sealed class ErrorMessage
        {
            internal ErrorMessage()
            {
                // empty
            }
        }
    }

    private HashSet<RequestHandler> GetHandlers(string queueName)
    {
        HashSet<RequestHandler>? requestHandlers = _requestHandlers.GetOrDefault(queueName, null);
        return requestHandlers is not null
            ? requestHandlers
            : [];
    }

    public RequestSender AddRequestSender()
    {
        Log.Debug("Adding request sender");
        return new RequestSender(this);
    }

    public sealed class RequestSender
    {
        private readonly Server _server;

        private bool _closed = false;

        internal RequestSender(Server server)
        {
            _server = server;
        }

        public void Remove()
        {
            Log.Debug("Removing request sender");
            _closed = true;
        }

        public async Task<string?>? RequestAsync(string queueName, long timestamp, string type, string data)
        {
            if (_closed)
            {
                throw new InvalidOperationException();
            }

            if (!ValidateQueueName(queueName))
            {
                return null;
            }

            if (!ValidateType(type))
            {
                return null;
            }

            if (!ValidateData(data))
            {
                return null;
            }

            HashSet<RequestHandler> requestHandlers;
            _server._requestHandlersLock.EnterReadLock();
            try
            {
                requestHandlers = _server.GetHandlers(queueName);
            }
            finally
            {
                _server._requestHandlersLock.ExitReadLock();
            }

            var request = new RequestHandler.RequestR(timestamp, type, data);

            foreach (RequestHandler requestHandler in requestHandlers)
            {
                TaskCompletionSource<string?> tcs = requestHandler.Request(request);
                string? response = await tcs.Task.ConfigureAwait(false);

                if (response is not null)
                {
                    return response;
                }
            }

            return null;
        }
    }

    private static bool ValidateQueueName(string queueName)
        => !string.IsNullOrWhiteSpace(queueName) && queueName.Length != 0 && !GetValitationRegex1().IsMatch(queueName) && !GetValitationRegex2().IsMatch(queueName);

    private static bool ValidateType(string type)
        => !string.IsNullOrWhiteSpace(type) && type.Length != 0 && !GetValitationRegex1().IsMatch(type) && !GetValitationRegex2().IsMatch(type);

    private static bool ValidateData(string str)
    {
        for (int i = 0; i < str.Length; i++)
            if (str[i] < 32 || str[i] >= 127)
                return false;

        return true;
    }

    [GeneratedRegex("[^A-Za-z0-9_\\-]")]
    private static partial Regex GetValitationRegex1();

    [GeneratedRegex("^[^A-Za-z0-9]")]
    private static partial Regex GetValitationRegex2();
}
