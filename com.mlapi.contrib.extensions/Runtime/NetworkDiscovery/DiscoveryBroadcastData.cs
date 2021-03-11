using MLAPI.Serialization;
using UnityEngine;

public struct DiscoveryBroadcastData : INetworkSerializable
{
    public long UniqueApplicationId;

    public void NetworkSerialize(NetworkSerializer serializer)
    {
        serializer.Serialize(ref UniqueApplicationId);
    }
}
