using System;
using System.Collections.Generic;
using System.Net;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;


namespace Netcode.Transports.WebSocket
{
    public class WebSocketServerConnectionBehavior : WebSocketBehavior
    {
        private static Dictionary<ulong, WebSocketPeer> Clients = new Dictionary<ulong, WebSocketPeer>();

        private static readonly object ConnectionLock = new object();

        private static ulong ClientIdCounter = 1;
        private static Queue<ulong> ReleasedClientIds = new Queue<ulong>();
        private static Queue<WebSocketEvent> EventQueue = new Queue<WebSocketEvent>();

        private static ulong GetNextClientId()
        {
            if (ReleasedClientIds.Count > 0)
            {
                return ReleasedClientIds.Dequeue();
            }
            else
            {
                return ClientIdCounter++;
            }
        }

        public static void ReleaseClientId(ulong clientId)
        {
            ReleasedClientIds.Enqueue(clientId);
        }

        public static void DisconnectClient(ulong clientId, CloseStatusCode code = CloseStatusCode.Normal, string reason = null)
        {
            lock (ConnectionLock)
            {
                if (Clients.ContainsKey(clientId))
                {
                    Clients[clientId].Close();
                }
            }
        }

        public static ulong Ping(ulong clientId)
        {
            lock (ConnectionLock)
            {
                if (Clients.ContainsKey(clientId))
                {
                    return Clients[clientId].Ping;
                }
            }

            return 0;
        }

        public static void Send(ulong clientId, ArraySegment<byte> data)
        {
            lock (ConnectionLock)
            {
                if (Clients.ContainsKey(clientId))
                {
                    if (data.Count < data.Array.Length || data.Offset > 0)
                    {
                        // WebSocket-Csharp cant handle this.
                        byte[] slimPayload = new byte[data.Count];

                        Buffer.BlockCopy(data.Array, data.Offset, slimPayload, 0, data.Count);

                        Clients[clientId].Send(slimPayload);
                    }
                    else
                    {
                        Clients[clientId].Send(data.Array);
                    }
                }
            }
        }

        public static WebSocketEvent Poll()
        {
            lock (ConnectionLock)
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
        }

        public IPEndPoint Endpoint { get; private set; }
        public WebSocketSharp.WebSocket Socket { get; private set; }
        public ulong ClientId { get; private set; }

        protected override void OnOpen()
        {
            Endpoint = Context.UserEndPoint;
            Socket = Context.WebSocket;

            lock (ConnectionLock)
            {
                ClientId = GetNextClientId();
                Clients[ClientId] = new WebSocketPeer(ClientId, Context);

                EventQueue.Enqueue(new WebSocketEvent()
                {
                    ClientId = ClientId,
                    Payload = null,
                    Type = WebSocketEvent.WebSocketEventType.Open,
                    Error = null,
                    Reason = null
                });
            }
        }

        protected override void OnClose(CloseEventArgs e)
        {
            lock (ConnectionLock)
            {
                if (Clients.ContainsKey(ClientId))
                {
                    Clients.Remove(ClientId);
                    ReleaseClientId(ClientId);

                    EventQueue.Enqueue(new WebSocketEvent()
                    {
                        ClientId = ClientId,
                        Payload = null,
                        Type = WebSocketEvent.WebSocketEventType.Close,
                        Error = null,
                        Reason = e.Reason
                    });
                }
            }
        }

        protected override void OnError(ErrorEventArgs e)
        {
            lock (ConnectionLock)
            {
                if (Clients.ContainsKey(ClientId))
                {
                    EventQueue.Enqueue(new WebSocketEvent()
                    {
                        ClientId = ClientId,
                        Payload = null,
                        Type = WebSocketEvent.WebSocketEventType.Error,
                        Error = e.Message,
                        Reason = null,
                    });
                }
            }
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            lock (ConnectionLock)
            {
                if (Clients.ContainsKey(ClientId))
                {
                    EventQueue.Enqueue(new WebSocketEvent()
                    {
                        ClientId = ClientId,
                        Payload = e.RawData,
                        Type = WebSocketEvent.WebSocketEventType.Payload,
                        Error = null,
                        Reason = null,
                    });
                }
            }
        }
    }
}
