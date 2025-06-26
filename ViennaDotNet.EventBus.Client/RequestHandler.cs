using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.EventBus.Client;

public sealed class RequestHandler
{
    private readonly EventBusClient _client;
    private readonly int _channelId;
    private readonly string _queueName;

    private readonly IHandler _handler;

    private volatile bool _closed = false;

    internal RequestHandler(EventBusClient client, int channelId, string queueName, IHandler handler)
    {
        _client = client;
        _channelId = channelId;
        _queueName = queueName;
        _handler = handler;
    }

    public void Close()
    {
        _closed = true;
        _client.RemoveSubscriber(_channelId);
        _client.SendMessage(_channelId, "CLOSE");
    }

    internal async Task<bool> HandleMessage(string message)
    {
        if (message == "ERR")
        {
            Close();
            _handler.Error();
            return true;
        }
        else
        {
            string[] fields = message.Split(':', 4);
            if (fields.Length != 4)
            {
                return false;
            }

            string requestIdString = fields[0];
            int requestId;
            try
            {
                requestId = int.Parse(requestIdString);
            }
            catch (FormatException)
            {
                return false;
            }

            if (requestId <= 0)
            {
                return false;
            }

            string timestampString = fields[1];
            long timestamp;
            try
            {
                timestamp = long.Parse(timestampString);
            }
            catch (FormatException)
            {
                return false;
            }

            if (timestamp < 0)
            {
                return false;
            }

            string type = fields[2];
            string data = fields[3];

            // TODO: remove requestAsync, beware awating it causes problems
            TaskCompletionSource<string?> responseCompletableFuture = _handler.RequestAsync(new Request(timestamp, type, data));
            responseCompletableFuture.Task.ContinueWith(task =>
            {
                if (!_closed)
                {
                    if (task.Result is not null)
                        _client.SendMessage(_channelId, "REP " + requestId + ":" + task.Result);
                    else
                        _client.SendMessage(_channelId, "NREP " + requestId);
                }
            }).Forget();

            return true;
        }
    }

    internal void Error()
    {
        _closed = true;
        _handler.Error();
    }

    public interface IHandler
    {
        TaskCompletionSource<string?> RequestAsync(Request request)
        {
            TaskCompletionSource<string?> completableFuture = new();
            new Thread(() =>
            {
                completableFuture.SetResult(Request(request).Result);
            }).Start();
            return completableFuture;
        }

        Task<string?> Request(Request request);

        void Error();
    }

    public class Handler : IHandler
    {
        public Func<Request, Task<string?>>? OnRequest;
        public Action? OnError;

        public Handler(Func<Request, Task<string?>>? onRequest, Action? onError)
        {
            OnRequest = onRequest;
            OnError = onError;
        }

        public Task<string?> Request(Request request)
            => OnRequest?.Invoke(request) ?? Task.FromResult<string?>(null);

        public void Error()
            => OnError?.Invoke();
    }

    public sealed class Request
    {
        public readonly long Timestamp;
        public readonly string Type;
        public readonly string Data;

        public Request(long timestamp, string type, string data)
        {
            Timestamp = timestamp;
            Type = type;
            Data = data;
        }
    }
}
