#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

namespace Netcode.Transports.WebRTC
{
    public class WebGLRTCPeerConnection : BaseCustomRTCPeerConnection
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };

        public Dictionary<string, Action<JsonElement>> EventListeners = new();

        public WebGLRTCPeerConnection(MonoBehaviour context, RTCIceServer[] iceServers, ulong clientId)
            : base(context, iceServers, clientId)
        {
            InitPeerEventHooks();

            var iceServersJson = JsonSerializer.Serialize(iceServers, JsonOptions);
            Debug.Log($"Calling bridge to create new peer: {iceServersJson}");
            WebGLRTCBridge.WebRTC_CreateNewPeer(iceServersJson, (int)clientId);
        }

        public override bool IsDataChannelOpen()
        {
            return WebGLRTCBridge.WebRTC_IsDataChannelOpen((int)ClientId);
        }

        public override void Send(byte[] data)
        {
            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = handle.AddrOfPinnedObject();
                WebGLRTCBridge.WebRTC_Send((int)ClientId, ptr, data.Length);
            }
            finally
            {
                handle.Free();
            }
        }

        public override async Task SetRemoteDescription(RTCSessionDescription description)
        {
            var descriptionJson = JsonSerializer.Serialize(description, JsonOptions);
            await WebGLRTCBridge.ExecuteJSAsync((callId) => WebGLRTCBridge.WebRTC_SetRemoteDescriptionAsync((int)ClientId, descriptionJson, callId));
        }

        public override void AddIceCandidate(RTCIceCandidateInit cand)
        {
            var candidateJson = JsonSerializer.Serialize(cand, JsonOptions);
            WebGLRTCBridge.WebRTC_AddIceCandidate((int)ClientId, candidateJson);
        }

        public override RTCIceConnectionState GetIceConnectionState()
        {
            return (RTCIceConnectionState)WebGLRTCBridge.WebRTC_GetIceConnectionState((int)ClientId);
        }

        public override async Task<RTCSessionDescription> PrepareOffer()
        {
            var offerJson = await WebGLRTCBridge.ExecuteJSAsync((callId) => WebGLRTCBridge.WebRTC_PrepareOfferAsync((int)ClientId, callId));
            var offerPayload = ParseJsonToElement(offerJson);

            return new RTCSessionDescription
            {
                sdp = offerPayload.GetProperty("sdp").GetString(),
                type = (RTCSdpType)offerPayload.GetProperty("type").GetInt16(),
            };
        }

        public override async Task<RTCSessionDescription> PrepareAnswer()
        {
            var answerJson = await WebGLRTCBridge.ExecuteJSAsync((callId) => WebGLRTCBridge.WebRTC_PrepareAnswerAsync((int)ClientId, callId));
            var answerPayload = ParseJsonToElement(answerJson);

            return new RTCSessionDescription
            {
                sdp = answerPayload.GetProperty("sdp").GetString(),
                type = (RTCSdpType)answerPayload.GetProperty("type").GetInt16(),
            };
        }

        public override void Close()
        {
            WebGLRTCBridge.WebRTC_Close((int)ClientId);
        }

        public override async Task<double> GetRTT()
        {
            var resultJson = await WebGLRTCBridge.ExecuteJSAsync((callId) => WebGLRTCBridge.WebRTC_GetRTTAsync((int)ClientId, callId));
            var result = ParseJsonToElement(resultJson);
            return result.GetProperty("rtt").GetDouble();
        }

        public void OnDataChannelMessageReceived(byte[] bytes)
        {
            TriggerOnDataChannelMessage(bytes);
        }

        private void InitPeerEventHooks()
        {
            EventListeners.Add("onicecandidate", (payload) =>
            {
                TriggerOnIceCandidate(new RTCIceCandidateInit
                {
                    candidate = payload.GetProperty("candidate").GetString(),
                    sdpMid = payload.GetProperty("sdpMid").GetString(),
                    sdpMLineIndex = payload.GetProperty("sdpMLineIndex").GetInt32()
                });
            });

            EventListeners.Add("oniceconnectionstatechange", (payload) =>
            {
                TriggerOnIceConnectionChange((RTCIceConnectionState)payload.GetProperty("state").GetInt16());
            });

            EventListeners.Add("datachannel.onopen", _ =>
            {
                TriggerOnDataChannelOpen();
            });

            EventListeners.Add("datachannel.onerror", (payload) =>
            {
                TriggerOnDataChannelError(new RTCError
                {
                    message = payload.GetProperty("message").GetString(),
                });
            });
        }

        private JsonElement ParseJsonToElement(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
    }
}
#endif
