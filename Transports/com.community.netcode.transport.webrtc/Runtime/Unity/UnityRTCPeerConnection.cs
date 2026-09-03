using System.Linq;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;
using NativeIceCandidateInit = Unity.WebRTC.RTCIceCandidateInit;
using NativePeerConnection = Unity.WebRTC.RTCPeerConnection;
using NativeSessionDescription = Unity.WebRTC.RTCSessionDescription;
using NativeSdpType = Unity.WebRTC.RTCSdpType;
using NativeIceServer = Unity.WebRTC.RTCIceServer;
using NativeIceCredentialType = Unity.WebRTC.RTCIceCredentialType;
using NativeDataChannelState = Unity.WebRTC.RTCDataChannelState;

namespace Netcode.Transports.WebRTC
{
    public class UnityRTCPeerConnection : BaseCustomRTCPeerConnection
    {
        private NativePeerConnection peer;
        private RTCDataChannel dataChannel;

        public UnityRTCPeerConnection(MonoBehaviour context, RTCIceServer[] iceServers, ulong clientId)
            : base(context, iceServers, clientId)
        {
            var config = CreateRtcConfiguration(iceServers);
            peer = new NativePeerConnection(ref config);
            peer.OnIceCandidate = cand =>
            {
                TriggerOnIceCandidate(new RTCIceCandidateInit
                {
                    candidate = cand.Candidate,
                    sdpMid = cand.SdpMid,
                    sdpMLineIndex = cand.SdpMLineIndex,
                });
            };
            peer.OnIceConnectionChange += state =>
            {
                TriggerOnIceConnectionChange((RTCIceConnectionState)state);
            };

            InitDataChannel();
        }

        private void InitDataChannel()
        {
            if (IsHost)
            {
                dataChannel = peer.CreateDataChannel("game");
                SetupDataChannelEvents();
            }
            else
            {
                peer.OnDataChannel = channel =>
                {
                    Debug.Log($"[WebRTCTransport] OnDataChannel available: {channel.Id}, {channel.Negotiated}, {channel.ReadyState}");

                    dataChannel = channel;
                    if (dataChannel.ReadyState == NativeDataChannelState.Open)
                    {
                        Debug.Log("[WebRTCTransport] Client DataChannel is already open");
                        TriggerOnDataChannelOpen();
                    }

                    SetupDataChannelEvents();
                };
            }
        }

        private void SetupDataChannelEvents()
        {
            dataChannel.OnOpen += () =>
            {
                TriggerOnDataChannelOpen();
            };

            dataChannel.OnMessage = bytes =>
            {
                TriggerOnDataChannelMessage(bytes);
            };

            dataChannel.OnError = error =>
            {
                TriggerOnDataChannelError(new RTCError
                {
                    errorType = (RTCErrorType)error.errorType,
                    message = error.message,
                });
            };
        }

        public override bool IsDataChannelOpen()
        {
            if (dataChannel == null)
            {
                return false;
            }

            return dataChannel.ReadyState == NativeDataChannelState.Open;
        }

        public override void Send(byte[] data)
        {
            dataChannel.Send(data);
        }

        public override async Task SetRemoteDescription(RTCSessionDescription description)
        {
            var unityDescription = new NativeSessionDescription
            {
                sdp = description.sdp,
                type = (NativeSdpType)description.type
            };
            await peer.SetRemoteDescriptionAsync(ref unityDescription, ctx);
        }

        public override void AddIceCandidate(RTCIceCandidateInit cand)
        {
            var candidate = new RTCIceCandidate(new NativeIceCandidateInit
            {
                candidate = cand.candidate,
                sdpMid = cand.sdpMid,
                sdpMLineIndex = cand.sdpMLineIndex,
            });
            peer.AddIceCandidate(candidate);
        }

        public override RTCIceConnectionState GetIceConnectionState()
        {
            return (RTCIceConnectionState)peer.IceConnectionState;
        }

        public override async Task<RTCSessionDescription> PrepareOffer()
        {
            var offer = await peer.CreateOfferAsync(ctx);
            await peer.SetLocalDescriptionAsync(ref offer, ctx);

            return new RTCSessionDescription
            {
                sdp = offer.sdp,
                type = (RTCSdpType)offer.type
            };
        }

        public override async Task<RTCSessionDescription> PrepareAnswer()
        {
            var answer = await peer.CreateAnswerAsync(ctx);
            await peer.SetLocalDescriptionAsync(ref answer, ctx);

            return new RTCSessionDescription
            {
                sdp = answer.sdp,
                type = (RTCSdpType)answer.type
            };
        }

        public override void Close()
        {
            peer?.Close();
            dataChannel?.Close();
        }

        public override async Task<double> GetRTT()
        {
            var statsReport = await peer.GetStatsAsync(ctx);
            foreach (var stat in statsReport.Stats.Values)
            {
                if (stat.Type == RTCStatsType.CandidatePair)
                {
                    var candidatePair = stat as RTCIceCandidatePairStats;
                    if (candidatePair != null)
                    {
                        return candidatePair.currentRoundTripTime;
                    }
                }
            }

            return 0;
        }

        private RTCConfiguration CreateRtcConfiguration(RTCIceServer[] iceServers)
        {
            NativeIceServer[] unityIceServers = iceServers.Select(iceServer =>
            {
                return new NativeIceServer
                {
                    username = iceServer.username,
                    credential = iceServer.credential,
                    credentialType = (NativeIceCredentialType)iceServer.credentialType,
                    urls = iceServer.urls
                };
            }).ToArray();

            return new RTCConfiguration
            {
                iceServers = unityIceServers,
            };
        }
    }
}
