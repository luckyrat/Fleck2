using System;

namespace Fleck2
{
    public class AvailabilityPingReceivedException : Exception
    {
        public AvailabilityPingReceivedException()
        {
        }
        
        public AvailabilityPingReceivedException(string message) : base(message)
        {
        }
        
        public AvailabilityPingReceivedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
