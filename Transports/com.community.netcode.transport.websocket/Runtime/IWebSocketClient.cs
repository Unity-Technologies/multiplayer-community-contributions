using System;
using System.Collections.Generic;
using WebSocketSharp;

namespace Netcode.Transports.WebSocket
{
    public interface IWebSocketClient
    {
        Queue<WebSocketEvent> EventQueue { get; }

        ulong WaitTime { get; }
        WebSocketState ReadyState { get; }

        void Connect();
        void Close(CloseStatusCode code = CloseStatusCode.Normal, string reason = null);
        void Send(ArraySegment<byte> data);
        WebSocketEvent Poll();
    }
}
