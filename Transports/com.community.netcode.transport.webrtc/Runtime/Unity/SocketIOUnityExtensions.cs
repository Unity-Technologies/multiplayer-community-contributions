using System;
using SocketIOClient;

namespace Netcode.Transports.WebRTC
{
    public static class SocketIOUnityExtensions
    {
        public static void OnUnityThread(this SocketIO socket, string eventName, Action<SocketIOResponse> callback)
        {
            socket.On(eventName, response =>
            {
                MainThreadDispatcher.Enqueue(() => callback(response));
            });
        }
    }
}
