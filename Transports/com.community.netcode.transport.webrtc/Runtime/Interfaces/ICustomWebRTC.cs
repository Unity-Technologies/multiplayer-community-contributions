using UnityEngine;

namespace Netcode.Transports.WebRTC
{
    public interface ICustomWebRTC
    {
        void Init(WebRTCTransport transport);
        BaseCustomRTCSignalClient CreateSignalClient(MonoBehaviour context, string url, string token);
        BaseCustomRTCPeerConnection CreatePeerConnection(MonoBehaviour context, RTCIceServer[] iceServers, ulong clientId);
    }
}
