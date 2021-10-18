using Unity.Netcode;
using UnityEngine;

public struct DiscoveryResponseData: INetworkSerializable
{
    public ushort Port;

    public string ServerName;
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Port);
        serializer.SerializeValue(ref ServerName);
    }
}
