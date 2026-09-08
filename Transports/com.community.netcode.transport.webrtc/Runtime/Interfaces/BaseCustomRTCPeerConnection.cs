using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Netcode.Transports.WebRTC
{
    public abstract class BaseCustomRTCPeerConnection
    {
        protected MonoBehaviour ctx;
        protected RTCIceServer[] availableIceServers;
        public readonly ulong ClientId;
        public bool IsHost => ClientId != 0;

        public event Action OnDataChannelOpen;
        public event Action<byte[]> OnDataChannelMessage;
        public event Action<RTCError> OnDataChannelError;
        public event Action<RTCIceCandidateInit> OnIceCandidate;
        public event Action<RTCIceConnectionState> OnIceConnectionChange;

        public BaseCustomRTCPeerConnection(MonoBehaviour context, RTCIceServer[] iceServers, ulong clientId)
        {
            ctx = context;
            availableIceServers = iceServers;
            ClientId = clientId;
        }

        public abstract bool IsDataChannelOpen();
        public abstract void Send(byte[] data);
        public abstract Task<double> GetRTT();
        public abstract Task SetRemoteDescription(RTCSessionDescription description);
        public abstract void AddIceCandidate(RTCIceCandidateInit iceCandidateInit);
        public abstract RTCIceConnectionState GetIceConnectionState();
        public abstract Task<RTCSessionDescription> PrepareOffer();
        public abstract Task<RTCSessionDescription> PrepareAnswer();
        public abstract void Close();

        protected void TriggerOnDataChannelOpen()
        {
            OnDataChannelOpen?.Invoke();
        }

        protected void TriggerOnDataChannelMessage(byte[] bytes)
        {
            OnDataChannelMessage?.Invoke(bytes);
        }

        protected void TriggerOnDataChannelError(RTCError error)
        {
            OnDataChannelError?.Invoke(error);
        }

        protected void TriggerOnIceCandidate(RTCIceCandidateInit iceCandidateInit)
        {
            OnIceCandidate?.Invoke(iceCandidateInit);
        }

        protected void TriggerOnIceConnectionChange(RTCIceConnectionState state)
        {
            OnIceConnectionChange?.Invoke(state);
        }
    }
}
