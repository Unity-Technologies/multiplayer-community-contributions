using MLAPI.Serialization;
using UnityEngine;

public struct DiscoveryResponseData: INetworkSerializable
{
    public ushort Port;

    public string ServerName;

    public void NetworkSerialize(NetworkSerializer serializer)
    {
        serializer.Serialize(ref Port);
        serializer.Serialize(ref ServerName);
    }
}
