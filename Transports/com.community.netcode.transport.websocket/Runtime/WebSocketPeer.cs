using System;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;

namespace Netcode.Transports.WebSocket
{
    public struct WebSocketPeer
    {
        public ulong ClientId;
        public WebSocketContext Context;

        public WebSocketPeer(ulong clientId, WebSocketContext context)
        {
            ClientId = clientId;
            Context = context;
        }

        internal void Close(CloseStatusCode code = CloseStatusCode.Normal, string reason = null)
        {
            Context.WebSocket.Close(code, reason);
        }

        internal void Send(byte[] data)
        {
            Context.WebSocket.Send(data);
        }

        internal ulong Ping
        {
            get
            {
                return (ulong)Context.WebSocket.WaitTime.Milliseconds;
            }
        }
    }
}
