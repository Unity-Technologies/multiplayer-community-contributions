using System;
using System.Collections.Generic;
using System.Net;
using MLAPI;
using MLAPI.Transports;
using MLAPI.WebSockets;

namespace WebSocketTransport
{
    public class WebSocketTransport : Transport
    {
        internal struct ClientEvent
        {
            public NetEventType Type;
            public ArraySegment<byte> Payload;
        }

        public string Url = "ws://127.0.0.1";
        public ushort Port;
        public override ulong ServerClientId => 0;
        private IWebSocketClient client;
        private NativeWebSocketServer server;

        private static readonly Queue<ClientEvent> clientEventQueue = new Queue<ClientEvent>();
        

        public override void DisconnectLocalClient()
        {
            if (client != null)
            {
                client.Close();
            }
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (server != null)
            {
                server.Close(clientId);
            }
        }

        public override void FlushSendQueue(ulong clientId)
        {
            
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            return 0;
        }

        public override void Init()
        {
            
        }

        public override NetEventType PollEvent(out ulong clientId, out string channelName, out ArraySegment<byte> payload)
        {
            payload = new ArraySegment<byte>();
            channelName = null;

            if (server != null)
            {
                WebSocketServerEvent @event = server.Poll();

                clientId = GetMLAPIClientId(@event.Id, false);

                switch (@event.Type)
                {
                    case WebSocketServerEventType.Open:
                        return NetEventType.Connect;
                    case WebSocketServerEventType.Close:
                        return NetEventType.Disconnect;
                    case WebSocketServerEventType.Payload:
                        return NetEventType.Data;
                }
            }

            if (client != null)
            {
                clientId = ServerClientId;

                if (clientEventQueue.Count > 0)
                {
                    ClientEvent @event = clientEventQueue.Dequeue();

                    return @event.Type;
                }
            }

            clientId = 0;

            return NetEventType.Nothing;
        }

        public override void Send(ulong clientId, ArraySegment<byte> data, string channelName, bool skipQueue)
        {
            if (server != null)
            {
                GetWebSocketConnectionDetails(clientId, out ulong connectionId);

                server.Send(connectionId, data);
            }

            if (client != null)
            {
                client.Send(data);
            }
        }

        public override void Shutdown()
        {
            clientEventQueue.Clear();

            if (server != null)
            {
                server.Shutdown();
            }

            client = null;
            server = null;
        }

        public override void StartClient()
        {
            string conUrl = Url;

            if (conUrl.EndsWith("/"))
            {
                conUrl = conUrl.Substring(0, conUrl.Length - 1);
            }

            conUrl += "/mlapi-connection";

            client = WebSocketClientFactory.Create(conUrl);

            client.SetOnOpen(() => 
            {
                clientEventQueue.Enqueue(new ClientEvent()
                {
                    Type = NetEventType.Connect,
                    Payload = new ArraySegment<byte>()
                });
            });

            client.SetOnClose((code) =>
            {
                clientEventQueue.Enqueue(new ClientEvent()
                {
                    Type = NetEventType.Disconnect,
                    Payload = new ArraySegment<byte>()
                });
            });

            client.SetOnPayload((payload) =>
            {
                clientEventQueue.Enqueue(new ClientEvent()
                {
                    Type = NetEventType.Disconnect,
                    Payload = payload
                });
            });

            client.Connect();
        }

        public override void StartServer()
        {
            server = NativeWebSocketServer.Instance;

            server.Start(IPAddress.Any, Port, "/mlapi-connection", NetworkingManager.Singleton.NetworkConfig.ServerX509Certificate);
        }


        public ulong GetMLAPIClientId(ulong connectionId, bool isServer)
        {
            if (isServer)
            {
                return ServerClientId;
            }
            else
            {
                return connectionId + 1;
            }
        }

        public void GetWebSocketConnectionDetails(ulong clientId, out ulong connectionId)
        {
            if (clientId == ServerClientId)
            {
                connectionId = ServerClientId;
            }
            else
            {
                connectionId = (clientId - 1);
            }
        }
    }
}
