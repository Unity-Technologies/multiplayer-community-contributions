#if UNITY_WEBGL && !UNITY_EDITOR
using AOT;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;

namespace Netcode.Transports.WebRTC
{
    public class WebGLRTCBridge : MonoBehaviour
    {
        private static WebRTCTransport s_Transport;
        private static Dictionary<string, TaskCompletionSource<string>> pendingCalls = new();
        private static PeerDataCallback s_PeerDataCallback;

        internal static void Bind(WebRTCTransport transport)
        {
            s_Transport = transport;
        }

        [DllImport("__Internal")]
        public static extern void WebRTC_InitBridge(string bridgeGameObjectName);

        [DllImport("__Internal")]
        public static extern void WebRTC_RegisterPeerDataCallback(IntPtr callbackPtr);

        [DllImport("__Internal")]
        public static extern void WebRTC_CreateSignalClient(string url, string token);

        [DllImport("__Internal")]
        public static extern void WebRTC_ConnectSignalClientAsync(string callId);

        [DllImport("__Internal")]
        public static extern void WebRTC_DisconnectSignalClient();

        [DllImport("__Internal")]
        public static extern void WebRTC_SendSignal(string eventName, string payload);

        [DllImport("__Internal")]
        public static extern void WebRTC_ListenForSignal(string eventName);

        [DllImport("__Internal")]
        public static extern void WebRTC_CreateNewPeer(string iceServersJson, int clientId);

        [DllImport("__Internal")]
        public static extern bool WebRTC_IsDataChannelOpen(int clientId);

        [DllImport("__Internal")]
        public static extern void WebRTC_Send(int clientId, IntPtr bytesPtr, int length);

        [DllImport("__Internal")]
        public static extern void WebRTC_SetRemoteDescriptionAsync(int clientId, string json, string callId);

        [DllImport("__Internal")]
        public static extern void WebRTC_AddIceCandidate(int clientId, string json);

        [DllImport("__Internal")]
        public static extern int WebRTC_GetIceConnectionState(int clientId);

        [DllImport("__Internal")]
        public static extern void WebRTC_PrepareOfferAsync(int clientId, string callId);

        [DllImport("__Internal")]
        public static extern void WebRTC_PrepareAnswerAsync(int clientId, string callId);

        [DllImport("__Internal")]
        public static extern void WebRTC_Close(int clientId);

        [DllImport("__Internal")]
        public static extern void WebRTC_GetRTTAsync(int clientId, string callId);

        private void Awake()
        {
            Debug.Log("WebGLRTCBridge::Awake");

            WebRTC_InitBridge(gameObject.name);

            s_PeerDataCallback = HandleIncomingPeerData;
            var fnPtr = Marshal.GetFunctionPointerForDelegate(s_PeerDataCallback);
            WebRTC_RegisterPeerDataCallback(fnPtr);
        }

        public static async Task<string> ExecuteJSAsync(Action<string> executor)
        {
            var callId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<string>();
            pendingCalls[callId] = tcs;

            executor(callId);
            return await tcs.Task;
        }

        public delegate void PeerDataCallback(int clientId, IntPtr dataPtr, int length);

        [MonoPInvokeCallback(typeof(PeerDataCallback))]
        public static void HandleIncomingPeerData(int clientId, IntPtr dataPtr, int length)
        {
            Debug.Log($"HandleIncomingPeerData ${clientId}: " + length);

            if (s_Transport == null)
            {
                Debug.LogWarning($"Received peer data for client {clientId}, but no WebRTCTransport is bound");
                return;
            }

            var peer = s_Transport.GetPeerFromClientId((ulong)clientId);
            var webGLPeer = peer as WebGLRTCPeerConnection;
            if (webGLPeer == null)
            {
                Debug.LogWarning($"Received peer data for client {clientId}, but the peer connection is not found");
                return;
            }

            byte[] managedData = new byte[length];
            Marshal.Copy(dataPtr, managedData, 0, length);
            webGLPeer.OnDataChannelMessageReceived(managedData);
        }

        public void OnJSAsyncResult(string json)
        {
            Debug.Log("OnJSAsyncResult: " + json);

            var payload = JsonUtility.FromJson<CallbackPayload>(json);
            if (pendingCalls.TryGetValue(payload.callId, out var tcs))
            {
                pendingCalls.Remove(payload.callId);
                tcs.SetResult(payload.result);
            }
        }

        public void OnIncomingSignalEvent(string json)
        {
            Debug.Log("Incoming Signal Event: " + json);

            var signalEvent = JsonUtility.FromJson<SignalEvent>(json);

            if (s_Transport == null || s_Transport.signalClient == null)
            {
                Debug.LogWarning($"Received signal event {signalEvent.eventName}, but the signal client is null");
                return;
            }

            if ((s_Transport.signalClient as WebGLRTCSignalClient).EventListeners.TryGetValue(signalEvent.eventName, out var listener))
            {
                using JsonDocument doc = JsonDocument.Parse(signalEvent.payload);
                listener(doc.RootElement.Clone());
            }
        }

        public void OnPeerEvent(string json)
        {
            Debug.Log("Incoming Peer Event: " + json);

            var peerEvent = JsonUtility.FromJson<PeerEvent>(json);

            if (s_Transport == null)
            {
                Debug.LogWarning($"Received peer event {peerEvent.eventName} for client {peerEvent.clientId}, but no WebRTCTransport is bound");
                return;
            }

            var peer = s_Transport.GetPeerFromClientId((ulong)peerEvent.clientId);
            if (peer == null)
            {
                Debug.LogWarning($"Received peer event {peerEvent.eventName} for client {peerEvent.clientId}, but the peer connection is not found");
                return;
            }

            if ((peer as WebGLRTCPeerConnection).EventListeners.TryGetValue(peerEvent.eventName, out var listener))
            {
                using JsonDocument doc = JsonDocument.Parse(peerEvent.payload);
                listener(doc.RootElement.Clone());
            }
        }

        [Serializable]
        private class CallbackPayload
        {
            public string callId;
            public string result;
        }

        [Serializable]
        private class SignalEvent
        {
            public string eventName;
            public string payload;
        }

        [Serializable]
        private class PeerEvent
        {
            public int clientId;
            public string eventName;
            public string payload;
        }
    }
}
#endif
