using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.EventBus.Client
{
    public class EventBusClientException : Exception
    {
        public EventBusClientException(string? message)
            : base(message)
        {
        }

        public EventBusClientException(string? message, Exception? innerException)
            : base(message, innerException)
        {
        }
    }
}
