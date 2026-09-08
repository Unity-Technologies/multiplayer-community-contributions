using System;
using UnityEngine;

namespace Netcode.Transports.WebRTC
{
    public class UnityWebRTC : ICustomWebRTC
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterBackend()
        {
            WebRTCTransport.RegisterNativeBackend(() => new UnityWebRTC());
        }

        public void Init(WebRTCTransport transport)
        {
        }

        public BaseCustomRTCSignalClient CreateSignalClient(MonoBehaviour context, string url, string token)
        {
            return new UnityRTCSignalClient(context, url, token);
        }

        public BaseCustomRTCPeerConnection CreatePeerConnection(MonoBehaviour context, RTCIceServer[] iceServers, ulong clientId)
        {
            return new UnityRTCPeerConnection(context, iceServers, clientId);
        }
    }
}
