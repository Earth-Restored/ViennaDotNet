using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.EventBus.Client
{
    public sealed class Subscriber
    {
        private EventBusClient client;
        int channelId;

        string queueName;

        private ISubscriberListener listener;

       internal Subscriber(EventBusClient client, int channelId, string queueName, ISubscriberListener listener)
        {
            this.client = client;
            this.channelId = channelId;
            this.queueName = queueName;
            this.listener = listener;
        }

        public void close()
        {
            client.removeSubscriber(channelId);
            client.sendMessage(channelId, "CLOSE");
        }

        internal bool handleMessage(string message)
        {
            if (message == "ERR")
            {
                close();
                listener.Error();
                return true;
            }
            else
            {
                string[] fields = message.Split(':', 3);
                if (fields.Length != 3)
                    return false;

                string timestampString = fields[0];
                if (!long.TryParse(fields[0], out long timestamp) || timestamp < 0)
                    return false;

                string type = fields[1];
                string data = fields[2];

                listener.Event(new Event(timestamp, type, data));

                return true;
            }
        }

        internal void error()
        {
            listener.Error();
        }

        public interface ISubscriberListener
        {
            void Event(Event _event);

            void Error();
        }

        public class SubscriberListener : ISubscriberListener
        {
            public event Action<Event>? OnEvent;
            public event Action? OnError;

            public SubscriberListener()
            {
            }
            public SubscriberListener(Action<Event>? _onEvent = null, Action? _onError = null)
            {
                OnEvent += _onEvent;
                OnError += _onError;
            }

            public void Error()
                => OnError?.Invoke();

            public void Event(Event _event)
                => OnEvent?.Invoke(_event);
        }

        public sealed class Event
        {
            public long timestamp;
            public string type;
            public string data;

            internal Event(long timestamp, string type, string data)
            {
                this.timestamp = timestamp;
                this.type = type;
                this.data = data;
            }
        }
    }
}
