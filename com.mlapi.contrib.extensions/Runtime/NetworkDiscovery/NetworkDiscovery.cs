using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using MLAPI.Serialization;
using MLAPI.Serialization.Pooled;
using UnityEngine;
using Random = UnityEngine.Random;

[DisallowMultipleComponent]
public abstract class NetworkDiscovery<TBroadCast, TResponse> : MonoBehaviour
    where TBroadCast : INetworkSerializable, new()
    where TResponse : INetworkSerializable, new()
{
    private enum MessageType : byte
    {
        BroadCast = 0,
        Response = 1,
    }

    UdpClient m_Client;

    [SerializeField] ushort m_Port = 47777;

    // This is long because unity inspector does not like ulong.
    [SerializeField]
    long m_UniqueApplicationId;

    /// <summary>
    /// Gets a value indicating whether the discovery is running.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Gets whether the discovery is in server mode.
    /// </summary>
    public bool IsServer { get; private set; }

    /// <summary>
    /// Gets whether the discovery is in client mode.
    /// </summary>
    public bool IsClient { get; private set; }

    public void OnApplicationQuit()
    {
        StopDiscovery();
    }

    void OnValidate()
    {
        if (m_UniqueApplicationId == 0)
        {
            var value1 = (long) Random.Range(int.MinValue, int.MaxValue);
            var value2 = (long) Random.Range(int.MinValue, int.MaxValue);
            m_UniqueApplicationId = value1 + (value2 << 32);
        }
    }

    public void ClientBroadcast(TBroadCast broadCast)
    {
        if (!IsClient)
        {
            throw new InvalidOperationException("Cannot send client broadcast while not running in client mode. Call StartClient first.");
        }

        IPEndPoint endPoint = new IPEndPoint(IPAddress.Broadcast, m_Port);

        using (PooledNetworkBuffer buffer = PooledNetworkBuffer.Get())
        {
            using (PooledNetworkWriter writer = PooledNetworkWriter.Get(buffer))
            {
                WriteHeader(writer, MessageType.BroadCast);

                broadCast.NetworkSerialize(writer.Serializer);
                var data = new ArraySegment<byte>(buffer.GetBuffer(), 0, (int) buffer.Length);

                try
                {
                    // This works because PooledBitStream.Get resets the position to 0 so the array segment will always start from 0.
                    m_Client.SendAsync(data.Array, data.Count, endPoint);
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }
            }
        }
    }

    /// <summary>
    /// Starts the discovery in server mode which will respond to client broadcasts searching for servers.
    /// </summary>
    public void StartServer()
    {
        StartDiscovery(true);
    }

    /// <summary>
    /// Starts the discovery in client mode. <see cref="ClientBroadcast"/> can be called to send out broadcasts to servers and the client will actively listen for responses.
    /// </summary>
    public void StartClient()
    {
        StartDiscovery(false);
    }

    public void StopDiscovery()
    {
        IsClient = false;
        IsServer = false;
        IsRunning = false;

        if (m_Client != null)
        {
            try
            {
                m_Client.Close();
            }
            catch (Exception)
            {
                // We don't care about socket exception here. Socket will always be closed after this.
            }

            m_Client = null;
        }
    }

    /// <summary>
    /// Gets called whenever a broadcast is received. Creates a response based on the incoming broadcast data.
    /// </summary>
    /// <param name="sender">The sender of the broadcast</param>
    /// <param name="broadCast">The broadcast data which was sent</param>
    /// <param name="response">The response to send back</param>
    /// <returns>True if a response should be sent back else false</returns>
    protected abstract bool ProcessBroadcast(IPEndPoint sender, TBroadCast broadCast, out TResponse response);

    /// <summary>
    /// Gets called when a response to a broadcast gets received
    /// </summary>
    /// <param name="sender">The sender of the response</param>
    /// <param name="response">The value of the response</param>
    protected abstract void ResponseReceived(IPEndPoint sender, TResponse response);

    void StartDiscovery(bool isServer)
    {
        StopDiscovery();

        IsServer = isServer;
        IsClient = !isServer;

        // If we are not a server we use the 0 port (let udp client assign a free port to us)
        var port = isServer ? m_Port : 0;

        m_Client = new UdpClient(port) {EnableBroadcast = true, MulticastLoopback = false};

        _ = ListenAsync(isServer ? ReceiveBroadcastAsync : new Func<Task>(ReceiveResponseAsync));

        IsRunning = true;
    }

    async Task ListenAsync(Func<Task> onReceiveTask)
    {
        while (true)
        {
            try
            {
                await onReceiveTask();
            }
            catch (ObjectDisposedException)
            {
                // socket has been closed
                break;
            }
            catch (Exception)
            {
            }
        }
    }

    async Task ReceiveResponseAsync()
    {
        UdpReceiveResult udpReceiveResult = await m_Client.ReceiveAsync();

        var segment = new ArraySegment<byte>(udpReceiveResult.Buffer, 0, udpReceiveResult.Buffer.Length);

        // Create a network buffer and put the received data into it
        using var buffer = PooledNetworkBuffer.Get();
        using var reader = PooledNetworkReader.Get(buffer);
        SetBufferContent(buffer, segment);

        try
        {
            if (ReadAndCheckHeader(reader, MessageType.Response) == false)
            {
                return;
            }

            var receivedResponse = new TResponse();
            receivedResponse.NetworkSerialize(reader.Serializer);
            ResponseReceived(udpReceiveResult.RemoteEndPoint, receivedResponse);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    async Task ReceiveBroadcastAsync()
    {
        UdpReceiveResult udpReceiveResult = await m_Client.ReceiveAsync();

        var segment = new ArraySegment<byte>(udpReceiveResult.Buffer, 0, udpReceiveResult.Buffer.Length);

        // Create a network buffer and put the received data into it
        using var buffer = PooledNetworkBuffer.Get();
        using var reader = PooledNetworkReader.Get(buffer);
        SetBufferContent(buffer, segment);

        try
        {
            if (ReadAndCheckHeader(reader, MessageType.BroadCast) == false)
            {
                return;
            }

            var receivedBroadcast = new TBroadCast();
            receivedBroadcast.NetworkSerialize(reader.Serializer);

            if (ProcessBroadcast(udpReceiveResult.RemoteEndPoint, receivedBroadcast, out TResponse response))
            {
                buffer.BitPosition = 0;
                using (PooledNetworkWriter writer = PooledNetworkWriter.Get(buffer))
                {
                    WriteHeader(writer, MessageType.Response);

                    response.NetworkSerialize(writer.Serializer);
                    var data = new ArraySegment<byte>(buffer.GetBuffer(), 0, (int) buffer.Length);

                    await m_Client.SendAsync(data.Array, data.Count, udpReceiveResult.RemoteEndPoint);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private static void SetBufferContent(NetworkBuffer buffer, ArraySegment<byte> arraySegment)
    {
        using (PooledNetworkWriter writer = PooledNetworkWriter.Get(buffer))
        {
            writer.WriteBytes(arraySegment.Array, arraySegment.Count);
        }

        buffer.BitPosition = 0;
    }

    private void WriteHeader(NetworkWriter writer, MessageType messageType)
    {
        // Serialize unique application id to make sure packet received is from same application.
        writer.WriteInt64(m_UniqueApplicationId);

        // Write a flag indicating whether this is a broadcast
        writer.WriteByte((byte) messageType);
    }

    private bool ReadAndCheckHeader(NetworkReader reader, MessageType expectedType)
    {
        var receivedApplicationId = reader.ReadInt64();
        if (receivedApplicationId != m_UniqueApplicationId)
        {
            return false;
        }

        byte messageType = (byte)reader.ReadByte();
        if (messageType != (byte) expectedType)
        {
            return false;
        }

        return true;
    }
}