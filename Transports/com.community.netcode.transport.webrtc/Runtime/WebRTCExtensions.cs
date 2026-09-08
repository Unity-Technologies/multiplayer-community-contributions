using System.Text.Json;

namespace Netcode.Transports.WebRTC
{
    public static class WebRTCExtensions
    {
        public static object ToObject(this RTCIceCandidateInit cand)
        {
            return new
            {
                cand.candidate,
                cand.sdpMid,
                cand.sdpMLineIndex,
            };
        }

        public static RTCIceCandidateInit ParseIceCandidateInit(this JsonElement data)
        {
            var candidateStr = data.GetProperty("candidate").GetString();
            var sdpMid = data.GetProperty("sdpMid").GetString();
            var sdpMLineIndex = data.GetProperty("sdpMLineIndex").GetInt32();

            return new RTCIceCandidateInit { candidate = candidateStr, sdpMid = sdpMid, sdpMLineIndex = sdpMLineIndex };
        }
    }
}
