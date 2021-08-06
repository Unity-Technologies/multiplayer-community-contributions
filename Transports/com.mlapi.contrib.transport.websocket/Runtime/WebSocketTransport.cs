using System;
using System.Collections.Generic;
using MLAPI.Transports;
using MLAPI.Transports.Tasks;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace MLAPI.Transports.WebSocket
{
    public class WebSocketTransport : NetworkTransport
    {
        private static WebSocketServer WebSocketServer = null;
        private static IWebSocketClient WebSocketClient = null;
        private static bool IsStarted = false;

        [Header("Transport")]
        public string ConnectAddress = "127.0.0.1";
        public ushort Port = 7777;

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

        public override void Init()
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

        public override NetworkEvent PollEvent(out ulong clientId, out NetworkChannel networkChannel, out ArraySegment<byte> payload, out float receiveTime)
        {
            var e = GetNextWebSocketEvent();

            clientId = e.ClientId;
            networkChannel = NetworkChannel.ChannelUnused;
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

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkChannel networkChannel)
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

        public override SocketTasks StartClient()
        {
            if (IsStarted)
            {
                throw new InvalidOperationException("Socket already started");
            }

            WebSocketClient = WebSocketClientFactory.Create($"ws://{ConnectAddress}:{Port}/mlapi");
            WebSocketClient.Connect();

            IsStarted = true;

            return SocketTask.Done.AsTasks();
        }

        public override SocketTasks StartServer()
        {
            if (IsStarted)
            {
                throw new InvalidOperationException("Socket already started");
            }

            WebSocketServer = new WebSocketServer(Port);
            WebSocketServer.AddWebSocketService<WebSocketServerConnectionBehavior>("/mlapi");
            WebSocketServer.Start();

            IsStarted = true;

            return SocketTask.Done.AsTasks();
        }
    }
}
