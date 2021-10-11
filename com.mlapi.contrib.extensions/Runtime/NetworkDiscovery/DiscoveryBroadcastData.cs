using Unity.Netcode;
using UnityEngine;

public struct DiscoveryBroadcastData : INetworkSerializable
{
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
    }
}
