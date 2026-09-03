using System;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;

namespace Netcode.Transports.WebRTC
{
    public static class UnityWebRTCExtensions
    {
        public static Task<RTCSessionDescription> CreateOfferAsync(this RTCPeerConnection pc, MonoBehaviour runner)
        {
            var op = pc.CreateOffer();
            return AwaitOp(op, runner).ContinueWith(t => op.Desc);
        }

        public static Task<RTCSessionDescription> CreateAnswerAsync(this RTCPeerConnection pc, MonoBehaviour runner)
        {
            var op = pc.CreateAnswer();
            return AwaitOp(op, runner).ContinueWith(t => op.Desc);
        }

        public static Task SetLocalDescriptionAsync(this RTCPeerConnection pc, ref RTCSessionDescription desc, MonoBehaviour runner)
        {
            var op = pc.SetLocalDescription(ref desc);
            return AwaitOp(op, runner);
        }

        public static Task SetRemoteDescriptionAsync(this RTCPeerConnection pc, ref RTCSessionDescription desc, MonoBehaviour runner)
        {
            var op = pc.SetRemoteDescription(ref desc);
            return AwaitOp(op, runner);
        }

        public static Task<RTCStatsReport> GetStatsAsync(this RTCPeerConnection pc, MonoBehaviour runner)
        {
            var op = pc.GetStats();
            return AwaitOp(op, runner).ContinueWith(t => op.Value);
        }

        private static Task AwaitOp(AsyncOperationBase op, MonoBehaviour runner)
        {
            var tcs = new TaskCompletionSource<bool>();
            runner.StartCoroutine(AwaitOpCoroutine(op, tcs));
            return tcs.Task;
        }

        private static System.Collections.IEnumerator AwaitOpCoroutine(AsyncOperationBase op, TaskCompletionSource<bool> tcs)
        {
            yield return op;

            if (op.IsError)
            {
                tcs.SetException(new Exception("WebRTC async operation failed: " + op.Error.message));
            }
            else
            {
                tcs.SetResult(true);
            }
        }
    }
}
