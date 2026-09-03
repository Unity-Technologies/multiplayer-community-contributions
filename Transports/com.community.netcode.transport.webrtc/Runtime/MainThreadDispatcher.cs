using System;
using System.Collections.Concurrent;

namespace Netcode.Transports.WebRTC
{
    internal static class MainThreadDispatcher
    {
        private static readonly ConcurrentQueue<Action> Queue = new();

        internal static void Enqueue(Action action)
        {
            Queue.Enqueue(action);
        }

        internal static void Dispatch()
        {
            while (Queue.TryDequeue(out var action))
            {
                action?.Invoke();
            }
        }
    }
}
