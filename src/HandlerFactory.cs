using System;
using Fleck2.Handlers;
using Fleck2.Interfaces;

namespace Fleck2
{
    public class HandlerFactory
    {
        public static IHandler BuildHandler(WebSocketHttpRequest request, Action<string> onMessage, Fleck2Extensions.Action onClose, Action<byte[]> onBinary)
        {
            var version = GetVersion(request);
            
            switch (version)
            {
                case "76":
                    return Draft76Handler.Create(request, onMessage);
                case "7":
                case "8":
                case "13":
                    return Hybi13Handler.Create(request, onMessage, onClose, onBinary);
            }

            if (request.Path == "/pingAvailabilityTest")
            {
                throw new AvailabilityPingReceivedException();
            }
            throw new WebSocketException(WebSocketStatusCodes.UnsupportedDataType);
        }
        
        public static string GetVersion(WebSocketHttpRequest request) 
        {
            string version;
            if (request.Headers.TryGetValue("Sec-WebSocket-Version", out version))
                return version;
                
            if (request.Headers.TryGetValue("Sec-WebSocket-Draft", out version))
                return version;
            
            if (request.Headers.ContainsKey("Sec-WebSocket-Key1"))
                return "76";
            
            return "75";
        }
    }
}

