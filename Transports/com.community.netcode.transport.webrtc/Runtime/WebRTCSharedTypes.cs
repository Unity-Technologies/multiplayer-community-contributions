using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Netcode.Transports.WebRTC
{
    /// <summary>
    ///
    /// </summary>
    public enum RTCErrorDetailType
    {
        /// <summary>
        ///
        /// </summary>
        DataChannelFailure,

        /// <summary>
        ///
        /// </summary>
        DtlsFailure,

        /// <summary>
        ///
        /// </summary>
        FingerprintFailure,

        /// <summary>
        ///
        /// </summary>
        IdpBadScriptFailure,

        /// <summary>
        ///
        /// </summary>
        IdpExecutionFailure,

        /// <summary>
        ///
        /// </summary>
        IdpLoadFailure,

        /// <summary>
        ///
        /// </summary>
        IdpNeedLogin,

        /// <summary>
        ///
        /// </summary>
        IdpTimeout,

        /// <summary>
        ///
        /// </summary>
        IdpTlsFailure,

        /// <summary>
        ///
        /// </summary>
        IdpTokenExpired,

        /// <summary>
        ///
        /// </summary>
        IdpTokenInvalid,

        /// <summary>
        ///
        /// </summary>
        SctpFailure,

        /// <summary>
        ///
        /// </summary>
        SdpSyntaxError,

        /// <summary>
        ///
        /// </summary>
        HardwareEncoderNotAvailable,

        /// <summary>
        ///
        /// </summary>
        HardwareEncoderError
    }

    /// <summary>
    ///
    /// </summary>
    public struct RTCError
    {
        /// <summary>
        ///
        /// </summary>
        public RTCErrorType errorType;

        /// <summary>
        ///
        /// </summary>
        public string message;
    }

    /// <summary>
    ///
    /// </summary>
    /// <seealso cref="RTCPeerConnection.ConnectionState"/>
    public enum RTCPeerConnectionState : int
    {
        /// <summary>
        ///
        /// </summary>
        New = 0,

        /// <summary>
        ///
        /// </summary>
        Connecting = 1,

        /// <summary>
        ///
        /// </summary>
        Connected = 2,

        /// <summary>
        ///
        /// </summary>
        Disconnected = 3,

        /// <summary>
        ///
        /// </summary>
        Failed = 4,

        /// <summary>
        ///
        /// </summary>
        Closed = 5
    }

    /// <summary>
    ///
    /// </summary>
    /// <seealso cref="RTCPeerConnection.IceConnectionState"/>
    public enum RTCIceConnectionState : int
    {
        /// <summary>
        ///
        /// </summary>
        New = 0,

        /// <summary>
        ///
        /// </summary>
        Checking = 1,

        /// <summary>
        ///
        /// </summary>
        Connected = 2,

        /// <summary>
        ///
        /// </summary>
        Completed = 3,

        /// <summary>
        ///
        /// </summary>
        Failed = 4,

        /// <summary>
        ///
        /// </summary>
        Disconnected = 5,

        /// <summary>
        ///
        /// </summary>
        Closed = 6,

        /// <summary>
        ///
        /// </summary>
        Max = 7
    }

    /// <summary>
    ///
    /// </summary>
    /// <seealso cref="RTCPeerConnection.GatheringState"/>
    public enum RTCIceGatheringState : int
    {
        /// <summary>
        ///
        /// </summary>
        New = 0,

        /// <summary>
        ///
        /// </summary>
        Gathering = 1,

        /// <summary>
        ///
        /// </summary>
        Complete = 2
    }

    /// <summary>
    ///
    /// </summary>
    /// <seealso cref="RTCPeerConnection.SignalingState"/>
    public enum RTCSignalingState : int
    {
        /// <summary>
        ///
        /// </summary>
        Stable = 0,

        /// <summary>
        ///
        /// </summary>
        HaveLocalOffer = 1,

        /// <summary>
        ///
        /// </summary>
        HaveLocalPrAnswer = 2,

        /// <summary>
        ///
        /// </summary>
        HaveRemoteOffer = 3,

        /// <summary>
        ///
        /// </summary>
        HaveRemotePrAnswer = 4,

        /// <summary>
        ///
        /// </summary>
        Closed = 5,
    }

    /// <summary>
    ///
    /// </summary>
    public enum RTCErrorType
    {
        /// <summary>
        ///
        /// </summary>
        None,

        /// <summary>
        ///
        /// </summary>
        UnsupportedOperation,

        /// <summary>
        ///
        /// </summary>
        UnsupportedParameter,

        /// <summary>
        ///
        /// </summary>
        InvalidParameter,

        /// <summary>
        ///
        /// </summary>
        InvalidRange,

        /// <summary>
        ///
        /// </summary>
        SyntaxError,

        /// <summary>
        ///
        /// </summary>
        InvalidState,

        /// <summary>
        ///
        /// </summary>
        InvalidModification,

        /// <summary>
        ///
        /// </summary>
        NetworkError,

        /// <summary>
        ///
        /// </summary>
        ResourceExhausted,

        /// <summary>
        ///
        /// </summary>
        InternalError,

        /// <summary>
        ///
        /// </summary>
        OperationErrorWithData
    }

    /// <summary>
    ///
    /// </summary>
    public enum RTCPeerConnectionEventType
    {
        /// <summary>
        ///
        /// </summary>
        ConnectionStateChange,

        /// <summary>
        ///
        /// </summary>
        DataChannel,

        /// <summary>
        ///
        /// </summary>
        IceCandidate,

        /// <summary>
        ///
        /// </summary>
        IceConnectionStateChange,

        /// <summary>
        ///
        /// </summary>
        Track
    }

    /// <summary>
    ///
    /// </summary>
    public enum RTCSdpType
    {
        /// <summary>
        ///
        /// </summary>
        Offer,

        /// <summary>
        ///
        /// </summary>
        Pranswer,

        /// <summary>
        ///
        /// </summary>
        Answer,

        /// <summary>
        ///
        /// </summary>
        Rollback
    }

    /// <summary>
    /// Please check the <see cref="RTCConfiguration.bundlePolicy"/> in the <see cref="RTCConfiguration"/> class.
    /// </summary>
    /// <seealso cref="RTCConfiguration.bundlePolicy"/>
    public enum RTCBundlePolicy : int
    {
        /// <summary>
        ///
        /// </summary>
        BundlePolicyBalanced = 0,

        /// <summary>
        ///
        /// </summary>
        BundlePolicyMaxBundle = 1,

        /// <summary>
        ///
        /// </summary>
        BundlePolicyMaxCompat = 2
    }

    /// <summary>
    /// Please check the <see cref="RTCDataChannel.ReadyState"/> in the <see cref="RTCDataChannel"/> class.
    /// </summary>
    /// <seealso cref="RTCDataChannel.ReadyState"/>
    public enum RTCDataChannelState
    {
        /// <summary>
        ///
        /// </summary>
        Connecting,

        /// <summary>
        ///
        /// </summary>
        Open,

        /// <summary>
        ///
        /// </summary>
        Closing,

        /// <summary>
        ///
        /// </summary>
        Closed
    }


    /// <summary>
    /// The RTCSessionDescription interface represents the setup of one side of a connection or a proposed connection.
    /// It contains a description type that identifies the negotiation stage it pertains to, along with the session's SDP (Session Description Protocol)
    /// details.
    /// </summary>
    /// <remarks>
    /// Establishing a connection between two parties involves swapping RTCSessionDescription objects,
    /// with each one proposing a set of connection setup options that the sender can accommodate.
    /// The connection setup is finalized when both parties agree on a particular configuration.
    /// </remarks>
    /// <example>
    ///     <code lang="cs"><![CDATA[
    ///         using System.Collections;
    ///         using System.Collections.Generic;
    ///         using System.Linq;
    ///         using UnityEngine;
    ///         using Unity.WebRTC;
    ///
    ///         class MediaStreamer : MonoBehaviour
    ///         {
    ///             private RTCPeerConnection _pc1;
    ///             private List<RTCRtpSender> pc1Senders;
    ///             private MediaStream videoStream;
    ///             private MediaStreamTrack track;
    ///             private DelegateOnNegotiationNeeded pc1OnNegotiationNeeded;
    ///             private bool videoUpdateStarted;
    ///
    ///             private void Start()
    ///             {
    ///                 pc1Senders = new List<RTCRtpSender>();
    ///                 pc1OnNegotiationNeeded = () => { StartCoroutine(PcOnNegotiationNeeded(_pc1)); };
    ///                 Call();
    ///             }
    ///
    ///             IEnumerator PcOnNegotiationNeeded(RTCPeerConnection pc)
    ///             {
    ///                 var op = pc.CreateOffer();
    ///                 yield return op;
    ///                 if (!op.IsError)
    ///                 {
    ///                     yield return StartCoroutine(OnCreateOfferSuccess(pc, op.Desc));
    ///                 }
    ///             }
    ///
    ///             private void Call()
    ///             {
    ///                 RTCConfiguration configuration = default;
    ///                 configuration.iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } };
    ///                 _pc1 = new RTCPeerConnection(ref configuration);
    ///                 _pc1.OnNegotiationNeeded = pc1OnNegotiationNeeded;
    ///
    ///                 videoStream = Camera.main.CaptureStream(1280, 720);
    ///                 track = videoStream.GetTracks().First();
    ///
    ///                 pc1Senders.Add(_pc1.AddTrack(track));
    ///                 if (!videoUpdateStarted)
    ///                 {
    ///                     StartCoroutine(WebRTC.Update());
    ///                     videoUpdateStarted = true;
    ///                 }
    ///             }
    ///
    ///             private IEnumerator OnCreateOfferSuccess(RTCPeerConnection pc, RTCSessionDescription desc)
    ///             {
    ///                 Debug.Log($"Offer created. SDP is: \n{desc.sdp}");
    ///                 var op = pc.SetLocalDescription(ref desc);
    ///                 yield return op;
    ///             }
    ///         }
    ///
    ///     ]]></code>
    /// </example>
    /// <seealso cref="RTCPeerConnection"/>
    /// <seealso cref="RTCRtpSender"/>
    public struct RTCSessionDescription
    {
        /// <summary>
        /// An enum that specifies the type of the session description. Refer to <see cref="RTCSdpType"/>.
        /// </summary>
        public RTCSdpType type;

        /// <summary>
        /// A string that holds the session's SDP information.
        /// </summary>
        [MarshalAs(UnmanagedType.LPStr)]
        public string sdp;
    }

    /// <summary>
    ///
    /// </summary>
    public struct RTCOfferAnswerOptions
    {
        /// <summary>
        ///
        /// </summary>
        public static RTCOfferAnswerOptions Default =
            new RTCOfferAnswerOptions { iceRestart = false, voiceActivityDetection = true };

        /// <summary>
        ///
        /// </summary>
        [MarshalAs(UnmanagedType.U1)]
        public bool iceRestart;

        /// <summary>
        ///
        /// </summary>
        /// <remarks>
        /// this property is not supported yet.
        /// </remarks>
        [MarshalAs(UnmanagedType.U1)]
        public bool voiceActivityDetection;
    }

    /// <summary>
    /// Please check the <see cref="RTCIceServer.credentialType"/> in the <see cref="RTCIceServer"/> struct.
    /// </summary>
    /// <seealso cref="RTCIceServer.credentialType"/>
    public enum RTCIceCredentialType
    {
        /// <summary>
        ///
        /// </summary>
        Password,

        /// <summary>
        ///
        /// </summary>
        OAuth
    }

    /// <summary>
    ///     Represents a configuration for an ICE server used within WebRTC connections.
    /// </summary>
    /// <remarks>
    ///     Represents a configuration for an ICE server used within WebRTC connections,
    ///     including authentication credentials and STUN/TURN server URLs.
    /// </remarks>
    /// <example>
    ///     <code lang="cs"><![CDATA[
    ///         RTCConfiguration configuration = default;
    ///         configuration.iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } };
    ///     ]]></code>
    /// </example>
    /// /// <seealso cref="RTCConfiguration"/>
    [Serializable]
    public struct RTCIceServer
    {
        /// <summary>
        ///     Specifies the credential for authenticating with the ICE server.
        /// </summary>
        [Tooltip("Optional: specifies the password to use when authenticating with the ICE server")]
        public string credential;

        /// <summary>
        ///     Specifies the type of credential used.
        /// </summary>
        [Tooltip("What type of credential the `password` value")]
        public RTCIceCredentialType credentialType;

        /// <summary>
        ///     An array containing the URLs of STUN or TURN servers to use for ICE negotiation.
        /// </summary>
        [Tooltip("Array to set URLs of your STUN/TURN servers")]
        public string[] urls;

        /// <summary>
        ///     Specifies the user name for authenticating with the ICE server.
        /// </summary>
        [Tooltip("Optional: specifies the username to use when authenticating with the ICE server")]
        public string username;
    }

    /// <summary>
    /// Please check the <see cref="RTCConfiguration.iceTransportPolicy"/> in the <see cref="RTCConfiguration"/> class.
    /// </summary>
    /// <seealso cref="RTCConfiguration.iceTransportPolicy"/>
    public enum RTCIceTransportPolicy : int
    {
        /// <summary>
        ///
        /// </summary>
        Relay = 1,
        /// <summary>
        ///
        /// </summary>
        All = 3
    }

    // RTCIceCandidate
    /// <summary>
    /// 
    /// </summary>
    [Serializable]
    public class RTCIceCandidateInit
    {
        /// <summary>
        /// 
        /// </summary>
        public string candidate;
        /// <summary>
        /// 
        /// </summary>
        public string sdpMid;
        /// <summary>
        /// 
        /// </summary>
        public int? sdpMLineIndex;
    }

    /// <summary>
    /// Enumerated type to specify a ICE component.
    /// </summary>
    /// <seealso cref="RTCIceCandidate"/>
    public enum RTCIceComponent : int
    {
        /// <summary>
        /// 
        /// </summary>
        Rtp = 1,
        /// <summary>
        /// 
        /// </summary>
        Rtcp = 2,
    }

    /// <summary>
    /// 
    /// </summary>
    public enum RTCIceProtocol : int
    {
        /// <summary>
        /// 
        /// </summary>
        Udp = 1,
        /// <summary>
        /// 
        /// </summary>
        Tcp = 2
    }

    /// <summary>
    /// 
    /// </summary>
    public enum RTCIceCandidateType
    {
        /// <summary>
        /// 
        /// </summary>
        Host,
        /// <summary>
        /// 
        /// </summary>
        Srflx,
        /// <summary>
        /// 
        /// </summary>
        Prflx,
        /// <summary>
        /// 
        /// </summary>
        Relay
    }

    /// <summary>
    /// 
    /// </summary>
    public enum RTCIceTcpCandidateType
    {
        /// <summary>
        /// 
        /// </summary>
        Active,
        /// <summary>
        /// 
        /// </summary>
        Passive,
        /// <summary>
        /// 
        /// </summary>
        So
    }

    internal static class CandidateExtention
    {
        public static RTCIceProtocol ParseRTCIceProtocol(this string src)
        {
            switch (src)
            {
                case "udp":
                    return RTCIceProtocol.Udp;
                case "tcp":
                    return RTCIceProtocol.Tcp;
                default:
                    throw new ArgumentException($"Invalid parameter: {src}");
            }
        }

        public static RTCIceCandidateType ParseRTCIceCandidateType(this string src)
        {
            switch (src)
            {
                case "local":
                    return RTCIceCandidateType.Host;
                case "stun":
                    return RTCIceCandidateType.Srflx;
                case "prflx":
                    return RTCIceCandidateType.Prflx;
                case "relay":
                    return RTCIceCandidateType.Relay;
                default:
                    throw new ArgumentException($"Invalid parameter: {src}");
            }
        }


        public static RTCIceTcpCandidateType? ParseRTCIceTcpCandidateType(this string src)
        {
            if (string.IsNullOrEmpty(src))
                return null;
            switch (src)
            {
                case "active":
                    return RTCIceTcpCandidateType.Active;
                case "passive":
                    return RTCIceTcpCandidateType.Passive;
                case "so":
                    return RTCIceTcpCandidateType.So;
                default:
                    throw new ArgumentException($"Invalid parameter: {src}");
            }
        }
    }
}
