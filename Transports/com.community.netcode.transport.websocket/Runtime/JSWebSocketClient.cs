#if UNITY_WEBGL
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WebSocketSharp;

namespace Netcode.Transports.WebSocket
{
    public class JSWebSocketClient : IWebSocketClient
    {
        public Queue<WebSocketEvent> EventQueue { get; } = new Queue<WebSocketEvent>();

        [DllImport("__Internal")]
        internal static extern void _Connect();

        [DllImport("__Internal")]
        internal static extern void _Close(CloseStatusCode code = CloseStatusCode.Normal, string reason = null);

        [DllImport("__Internal")]
        internal static extern void _Send(byte[] data, int offset, int count);

        [DllImport("__Internal")]
        internal static extern WebSocketState _GetState();

        public ulong WaitTime => 0;

        public WebSocketState ReadyState => _GetState();

        public void Connect()
        {
            _Connect();
        }

        public void Close(CloseStatusCode code = CloseStatusCode.Normal, string reason = null)
        {
            _Close(code, reason);
        }

        public void Send(ArraySegment<byte> data)
        {
            _Send(data.Array, data.Offset, data.Count);
        }

        public WebSocketEvent Poll()
        {
            if (EventQueue.Count > 0)
            {
                return EventQueue.Dequeue();
            }
            else
            {
                return new WebSocketEvent()
                {
                    ClientId = 0,
                    Payload = null,
                    Type = WebSocketEvent.WebSocketEventType.Nothing,
                    Error = null,
                    Reason = null
                };
            }
        }

        public void OnOpen()
        {
            EventQueue.Enqueue(new WebSocketEvent()
            {
                ClientId = 0,
                Payload = null,
                Type = WebSocketEvent.WebSocketEventType.Open,
                Error = null,
                Reason = null
            });
        }

        public void OnMessage(ArraySegment<byte> data)
        {
            EventQueue.Enqueue(new WebSocketEvent()
            {
                ClientId = 0,
                Payload = data.Array,
                Type = WebSocketEvent.WebSocketEventType.Payload,
                Error = null,
                Reason = null
            });
        }

        public void OnError(string error)
        {
            EventQueue.Enqueue(new WebSocketEvent()
            {
                ClientId = 0,
                Payload = null,
                Type = WebSocketEvent.WebSocketEventType.Error,
                Error = error,
                Reason = null
            });
        }

        public void OnClose(CloseStatusCode code)
        {
            EventQueue.Enqueue(new WebSocketEvent()
            {
                ClientId = 0,
                Payload = null,
                Type = WebSocketEvent.WebSocketEventType.Close,
                Error = null,
                Reason = null
            });
        }
    }
}
#endif