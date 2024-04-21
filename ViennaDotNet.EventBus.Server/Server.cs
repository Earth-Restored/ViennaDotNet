using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.EventBus.Server
{
    public class Server
    {
        private readonly ReaderWriterLockSlim subscribersLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private readonly Dictionary<string, HashSet<Subscriber>> subscribers = new();

        public Subscriber? addSubscriber(string queueName, Action<Subscriber.Message> consumer)
        {
            if (!validateQueueName(queueName))
                return null;

            Log.Debug($"Adding subscriber for {queueName}");

            subscribersLock.EnterWriteLock();

            Subscriber subscriber = new Subscriber(this, queueName, consumer);
            subscribers.ComputeIfAbsent(queueName, name => new())!.Add(subscriber);

            subscribersLock.ExitWriteLock();

            return subscriber;
        }

        public sealed class Subscriber
        {
            private readonly Server server;

            private readonly string queueName;
            private readonly Action<Message> consumer;
            private bool ended = false;

            internal Subscriber(Server server, string queueName, Action<Message> consumer)
            {
                this.server = server;
                this.queueName = queueName;
                this.consumer = consumer;
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            public void remove()
            {
                ended = true;

                new Thread(() =>
                {
                    Log.Debug("Removing subscriber");
                    server.subscribersLock.EnterWriteLock();
                    HashSet<Subscriber>? subscribers = server.subscribers.GetOrDefault(queueName, null);
                    if (subscribers != null)
                        subscribers.Remove(this);

                    server.subscribersLock.ExitWriteLock();
                }).Start();
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            internal void push(EntryMessage entryMessage)
            {
                if (!ended)
                    consumer.Invoke(entryMessage);
            }

            [MethodImpl(MethodImplOptions.Synchronized)]
            internal void error()
            {
                if (!ended)
                {
                    consumer.Invoke(new ErrorMessage());
                    ended = true;
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
                public readonly long timestamp;
                public readonly string type;
                public readonly string data;

                internal EntryMessage(long timestamp, string type, string data)
                {
                    this.timestamp = timestamp;
                    this.type = type;
                    this.data = data;
                }
            }

            public class ErrorMessage : Message
            {
                internal ErrorMessage()
                {
                    // empty
                }
            }
        }

        private IEnumerable<Subscriber> getSubscribers(string queueName)
        {
            HashSet<Subscriber>? subscribers = this.subscribers.GetOrDefault(queueName, null);
            if (subscribers != null)
                return subscribers;
            else
                return Enumerable.Empty<Subscriber>();
        }

        public Publisher addPublisher()
        {
            Log.Debug("Adding publisher");
            return new Publisher(this);
        }

        public sealed class Publisher
        {
            private readonly Server server;
            private bool closed = false;

            public Publisher(Server server)
            {
                this.server = server;
            }

            public void remove()
            {
                Log.Debug("Removing publisher");
                closed = true;
            }

            public bool publish(string queueName, long timestamp, string type, string data)
            {
                if (closed)
                    throw new Exception();

                if (!validateQueueName(queueName))
                    return false;

                if (!validateType(type))
                    return false;

                if (!validateData(data))
                    return false;

                server.subscribersLock.EnterReadLock();

                Subscriber.EntryMessage message = new Subscriber.EntryMessage(timestamp, type, data);
                server.getSubscribers(queueName).ForEach(subscriber => subscriber.push(message));

                server.subscribersLock.ExitReadLock();

                return true;
            }
        }

        private static bool validateQueueName(string queueName)
        {
            if (string.IsNullOrWhiteSpace(queueName) || queueName.Length == 0 || Regex.IsMatch(queueName, "[^A-Za-z0-9_\\-]") || Regex.IsMatch(queueName, "^[^A-Za-z0-9]"))
                return false;

            return true;
        }

        private static bool validateType(string type)
        {
            if (string.IsNullOrWhiteSpace(type) || type.Length == 0 || Regex.IsMatch(type, "[^A-Za-z0-9_\\-]") || Regex.IsMatch(type, "^[^A-Za-z0-9]"))
                return false;

            return true;
        }

        private static bool validateData(string str)
        {
            for (int i = 0; i < str.Length; i++)
                if (str[i] < 32 || str[i] >= 127)
                    return false;

            return true;
        }
    }
}
