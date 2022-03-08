using System;
using Unity.Netcode;
using UnityEngine;
using WebSocketSharp.Server;

namespace Netcode.Transports.WebSocket
{
    public class WebSocketTransport : NetworkTransport
    {
        private static WebSocketServer WebSocketServer = null;
        private static IWebSocketClient WebSocketClient = null;
        private static bool IsStarted = false;

        [Header("Transport")]
        public string ConnectAddress = "127.0.0.1";
        public ushort Port = 7777;
        public bool SecureConnection = false;

        public override ulong ServerClientId => 0;

        public override void DisconnectLocalClient()
        {
            WebSocketClient.Close();
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            WebSocketServerConnectionBehavior.DisconnectClient(clientId);
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            if (WebSocketClient != null)
            {
                return WebSocketClient.WaitTime;
            }
            else if (WebSocketServer != null)
            {
                return WebSocketServerConnectionBehavior.Ping(clientId);
            }

            return 0;
        }

        public override void Initialize(NetworkManager networkManager = null)
        {

        }

        public WebSocketEvent GetNextWebSocketEvent()
        {
            if (WebSocketClient != null)
            {
                return WebSocketClient.Poll();
            }

            return WebSocketServerConnectionBehavior.Poll();
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            var e = GetNextWebSocketEvent();

            clientId = e.ClientId;
            receiveTime = Time.realtimeSinceStartup;

            if (e.Payload != null)
            {
                payload = new ArraySegment<byte>(e.Payload);
            }
            else
            {
                payload = new ArraySegment<byte>();
            }

            return e.GetNetworkEvent();
        }

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            if (clientId == ServerClientId)
            {
                WebSocketClient.Send(data);
            }
            else
            {
                WebSocketServerConnectionBehavior.Send(clientId, data);
            }
        }

        public override void Shutdown()
        {
            if (WebSocketClient != null)
            {
                WebSocketClient.Close();
            }
            else if (WebSocketServer != null)
            {
                WebSocketServer.Stop();
            }
        }

        public override bool StartClient()
        {
            if (IsStarted)
            {
                throw new InvalidOperationException("Socket already started");
            }

            var protocol = SecureConnection ? "wss" : "ws";
            WebSocketClient = WebSocketClientFactory.Create($"{protocol}://{ConnectAddress}:{Port}/netcode");
            WebSocketClient.Connect();

            IsStarted = true;

            return true;
        }

        public override bool StartServer()
        {
            if (IsStarted)
            {
                throw new InvalidOperationException("Socket already started");
            }

            WebSocketServer = new WebSocketServer(Port);
            WebSocketServer.AddWebSocketService<WebSocketServerConnectionBehavior>("/netcode");
            WebSocketServer.Start();

            IsStarted = true;

            return true;
        }
    }
}
