using System;
using Unity.Netcode;

namespace Netcode.Transports.WebSocket
{
    public class WebSocketEvent
    {
        public enum WebSocketEventType
        {
            Nothing,
            Open,
            Close,
            Payload,
            Error,
        }

        public WebSocketEventType Type;
        public ulong ClientId;
        public byte[] Payload;
        public string Error;
        public string Reason;

        public NetworkEvent GetNetworkEvent()
        {
            switch (Type)
            {
                case WebSocketEventType.Payload:
                    return NetworkEvent.Data;
                case WebSocketEventType.Nothing:
                case WebSocketEventType.Error:
                    return NetworkEvent.Nothing;
                case WebSocketEventType.Open:
                    return NetworkEvent.Connect;
                case WebSocketEventType.Close:
                    return NetworkEvent.Disconnect;
            }

            return NetworkEvent.Nothing;
        }
    }
}
