using SocketIOClient;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

namespace Netcode.Transports.WebRTC
{
    public class UnityRTCSignalClient : BaseCustomRTCSignalClient
    {
        private SocketIO client;

        public UnityRTCSignalClient(MonoBehaviour context, string url, string token) : base(context, url, token)
        {
            client = new SocketIO(url, new SocketIOOptions
            {
                Query = new Dictionary<string, string>
                {
                    { "token", token }
                },
                Transport = SocketIOClient.Transport.TransportProtocol.WebSocket
            });
        }

        public override Task Connect()
        {
            return client?.ConnectAsync();
        }

        public override Task Disconnect()
        {
            return client?.DisconnectAsync();
        }

        public override Task Emit(string eventName, params object[] payload)
        {
            return client.EmitAsync(eventName, payload);
        }

        public override void On(string eventName, Action<JsonElement> action)
        {
            client.OnUnityThread(eventName, response =>
            {
                action(response.GetValue());
            });
        }
    }
}
