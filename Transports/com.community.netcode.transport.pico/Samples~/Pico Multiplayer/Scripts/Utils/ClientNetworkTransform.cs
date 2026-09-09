// see: https://docs-multiplayer.unity3d.com/netcode/current/components/networktransform/index.html
using Unity.Netcode.Components;
using UnityEngine;

namespace Netcode.Transports.Pico.Sample.PicoMultiplayer
{
    /// <summary>
    /// Used for syncing a transform with client side changes. This includes host. Pure server as owner isn't supported by this. Please use NetworkTransform
    /// for transforms that'll always be owned by the server.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }    
    }
}

