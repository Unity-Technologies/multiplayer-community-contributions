using UnityEngine;

namespace Netcode.Transports.WebRTC
{
    internal static class ComponentUtility
    {
        internal static T GetOrAddComponent<T>(Component context) where T : Component
        {
            var component = context.GetComponent<T>();
            if (component == null)
            {
                component = context.gameObject.AddComponent<T>();
            }

            return component;
        }
    }
}
