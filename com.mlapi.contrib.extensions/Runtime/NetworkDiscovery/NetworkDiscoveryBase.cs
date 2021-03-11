using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MLAPI.Serialization;
using MLAPI.Serialization.Pooled;
using UnityEngine;

[DisallowMultipleComponent]
public abstract class NetworkDiscoveryBase<TBroadCast, TResponse> : MonoBehaviour
    where TBroadCast : INetworkSerializable, new()
    where TResponse : INetworkSerializable, new()
{
    UdpClient m_Client;

    [SerializeField]
    ushort m_Port = 47777;

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
                broadCast.NetworkSerialize(writer.Serializer);
                var data = new ArraySegment<byte>(buffer.GetBuffer(), 0, (int)buffer.Length);

                try
                {
                    // This works because PooledBitStream.Get resets the position to 0 so the array segment will always start from 0.
                    m_Client.SendAsync(data.Array, data.Count, endPoint);
                }
                catch (Exception e)
                {
                    // No need to throw when we encounter a networking exception.
                    Debug.LogWarning(e);
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

        m_Client = new UdpClient(m_Port) { EnableBroadcast = true, MulticastLoopback = false, };

        _ = ListenAsync( isServer ?  ReceiveBroadcastAsync: new Func<Task>(ReceiveResponseAsync));

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
            catch (Exception) { }
        }
    }

    async Task ReceiveResponseAsync()
    {
        UdpReceiveResult udpReceiveResult = await m_Client.ReceiveAsync();

        var ipEndPoint = m_Client.Client.LocalEndPoint as IPEndPoint;

        if (Equals(ipEndPoint.Address, udpReceiveResult.RemoteEndPoint.Address))
        {
            // We received data from ourselves, ignore it
            return;
        }

        if (ipEndPoint.Address == IPAddress.None)
        {
            
        }

        Debug.Log(udpReceiveResult.RemoteEndPoint.Address);
        Debug.Log(ipEndPoint.Address);

        var segment = new ArraySegment<byte>(udpReceiveResult.Buffer, 0, udpReceiveResult.Buffer.Length);

        try
        {
            var receivedResponse = SerializableFromArraySegment<TResponse>(segment);
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

        var ipEndPoint = m_Client.Client.LocalEndPoint as IPEndPoint;

        if (ipEndPoint.Address == udpReceiveResult.RemoteEndPoint.Address)
        {
            // We received data from ourselves, ignore it
            return;
        }

        var segment = new ArraySegment<byte>(udpReceiveResult.Buffer, 0, udpReceiveResult.Buffer.Length);

        try
        {
            var receivedBroadcast = SerializableFromArraySegment<TBroadCast>(segment);
            if (ProcessBroadcast(udpReceiveResult.RemoteEndPoint, receivedBroadcast, out TResponse response))
            {
                var data = SerializableToArraySegment(response);
                m_Client.Send(data.Array, data.Count, udpReceiveResult.RemoteEndPoint);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ArraySegment<byte> SerializableToArraySegment<T>(T serializable) where T : INetworkSerializable
    {
        using (PooledNetworkBuffer buffer = PooledNetworkBuffer.Get())
        {
            using (PooledNetworkWriter writer = PooledNetworkWriter.Get(buffer))
            {
                serializable.NetworkSerialize(writer.Serializer);
                return new ArraySegment<byte>(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
        }
    }

    private static T SerializableFromArraySegment<T>(ArraySegment<byte> arraySegment) where T : INetworkSerializable, new()
    {
        using (PooledNetworkBuffer buffer = PooledNetworkBuffer.Get())
        {
            using (PooledNetworkWriter writer = PooledNetworkWriter.Get(buffer))
            {
                writer.WriteBytes(arraySegment.Array, arraySegment.Count);
            }
            buffer.BitPosition = 0;
            using (PooledNetworkReader reader = PooledNetworkReader.Get(buffer))
            {
                var serializable = new T();
                serializable.NetworkSerialize(reader.Serializer);
                return serializable;
            }
        }
    }
}
