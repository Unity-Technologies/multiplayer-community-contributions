using System;
using System.Security.Cryptography.X509Certificates;
using Unity.Netcode;
using UnityEngine;
using WebSocketSharp.Server;

namespace Netcode.Transports.WebSocket
{
    public class WebSocketTransport : NetworkTransport
    {
        private WebSocketServer WebSocketServer = null;
        private IWebSocketClient WebSocketClient = null;
        private bool IsStarted = false;

        [Header("Transport")]
        public string ConnectAddress = "127.0.0.1";
        public string Path = "/netcode";
        public ushort Port = 7777;
        public bool SecureConnection = false;
        public bool AllowForwardedRequest;
        public string CertificateBase64String;

        public override ulong ServerClientId => 0;

        public override void DisconnectLocalClient()
        {
            if (WebSocketClient == null) return;
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
            IsStarted = false;
        }

        public override bool StartClient()
        {
            if (IsStarted)
            {
                throw new InvalidOperationException("Socket already started");
            }

            var protocol = SecureConnection ? "wss" : "ws";
            WebSocketClient = WebSocketClientFactory.Create($"{protocol}://{ConnectAddress}:{Port}{Path}");
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
            
            WebSocketServer = new WebSocketServer(Port, SecureConnection);
            WebSocketServer.AllowForwardedRequest = AllowForwardedRequest;
            WebSocketServer.AddWebSocketService<WebSocketServerConnectionBehavior>(Path);
            if (!string.IsNullOrEmpty(CertificateBase64String))
            {
                var bytes = Convert.FromBase64String(CertificateBase64String);
                WebSocketServer.SslConfiguration.ServerCertificate = new X509Certificate2(bytes);
            }
            WebSocketServer.Start();

            IsStarted = true;

            return true;
        }
    }
}
