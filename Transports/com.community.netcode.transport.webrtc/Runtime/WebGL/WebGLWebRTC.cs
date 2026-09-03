using System;
using UnityEngine;

namespace Netcode.Transports.WebRTC
{
    public class WebGLWebRTC : ICustomWebRTC
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        WebGLRTCBridge bridge;
#endif

        public void Init(WebRTCTransport transport)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            bridge = ComponentUtility.GetOrAddComponent<WebGLRTCBridge>(transport);
            WebGLRTCBridge.Bind(transport);
#else
            throw new NotImplementedException();
#endif
        }

        public BaseCustomRTCSignalClient CreateSignalClient(MonoBehaviour context, string url, string token)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new WebGLRTCSignalClient(context, url, token);
#else
            throw new NotImplementedException();
#endif
        }

        public BaseCustomRTCPeerConnection CreatePeerConnection(MonoBehaviour context, RTCIceServer[] iceServers, ulong clientId)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new WebGLRTCPeerConnection(context, iceServers, clientId);
#else
            throw new NotImplementedException();
#endif
        }
    }
}
