using System;

namespace Netcode.Transports.WebSocket
{
    public class WebSocketException : Exception
    {
        public WebSocketException()
        {
        }

        public WebSocketException(string message) : base(message)
        {
        }

        public WebSocketException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
