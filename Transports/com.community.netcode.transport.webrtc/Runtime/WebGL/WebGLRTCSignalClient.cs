#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

namespace Netcode.Transports.WebRTC
{
    public class WebGLRTCSignalClient : BaseCustomRTCSignalClient
    {
        public Dictionary<string, Action<JsonElement>> EventListeners = new();

        public WebGLRTCSignalClient(MonoBehaviour context, string url, string token) : base(context, url, token)
        {
            WebGLRTCBridge.WebRTC_CreateSignalClient(url, token);
        }

        public override async Task Connect()
        {
            await WebGLRTCBridge.ExecuteJSAsync(callId => WebGLRTCBridge.WebRTC_ConnectSignalClientAsync(callId));
        }

        public override Task Disconnect()
        {
            WebGLRTCBridge.WebRTC_DisconnectSignalClient();
            return Task.CompletedTask;
        }

        public override Task Emit(string eventName, params object[] payloads)
        {
            if (payloads.Length > 0)
            {
                var payload = JsonSerializer.Serialize(payloads[0]);
                WebGLRTCBridge.WebRTC_SendSignal(eventName, payload);
            }

            return Task.CompletedTask;
        }

        public override void On(string eventName, Action<JsonElement> action)
        {
            EventListeners.Add(eventName, action);
            WebGLRTCBridge.WebRTC_ListenForSignal(eventName);
        }
    }
}
#endif
