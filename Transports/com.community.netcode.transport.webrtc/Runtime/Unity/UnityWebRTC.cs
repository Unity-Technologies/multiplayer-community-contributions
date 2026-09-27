using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace Netcode.Transports.WebRTC
{
    [Preserve]
    public class UnityWebRTC : ICustomWebRTC
    {
        [Preserve]
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
