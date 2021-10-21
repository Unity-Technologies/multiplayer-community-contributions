using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Unity.Netcode;
using UnityEngine;

namespace Netcode.Transports.LiteNetLib
{
    public class LiteNetLibTransport : NetworkTransport, INetEventListener
    {
        enum HostType
        {
            None,
            Server,
            Client
        }
        
        [Tooltip("The port to listen on (if server) or connect to (if client)")]
        public ushort Port = 7777;
        [Tooltip("The address to connect to as client; ignored if server")]
        public string Address = "127.0.0.1";
        [Tooltip("Interval between ping packets used for detecting latency and checking connection, in seconds")]
        public float PingInterval = 1f;
        [Tooltip("Maximum duration for a connection to survive without receiving packets, in seconds")]
        public float DisconnectTimeout = 5f;
        [Tooltip("Delay between connection attempts, in seconds")]
        public float ReconnectDelay = 0.5f;
        [Tooltip("Maximum connection attempts before client stops and reports a disconnection")]
        public int MaxConnectAttempts = 10;
        [Tooltip("Size of default buffer for decoding incoming packets, in bytes")]
        public int MessageBufferSize = 1024 * 5;
        [Tooltip("Simulated chance for a packet to be \"lost\", from 0 (no simulation) to 100 percent")]
        public int SimulatePacketLossChance = 0;
        [Tooltip("Simulated minimum additional latency for packets in milliseconds (0 for no simulation)")]
        public int SimulateMinLatency = 0;
        [Tooltip("Simulated maximum additional latency for packets in milliseconds (0 for no simulation")]
        public int SimulateMaxLatency = 0;

        readonly Dictionary<ulong, NetPeer> m_Peers = new Dictionary<ulong, NetPeer>();

        NetManager m_NetManager;

        byte[] m_MessageBuffer;

        public override ulong ServerClientId => 0;
        HostType m_HostType;
        
        void OnValidate()
        {
            PingInterval = Math.Max(0, PingInterval);
            DisconnectTimeout = Math.Max(0, DisconnectTimeout);
            ReconnectDelay = Math.Max(0, ReconnectDelay);
            MaxConnectAttempts = Math.Max(0, MaxConnectAttempts);
            MessageBufferSize = Math.Max(0, MessageBufferSize);
            SimulatePacketLossChance = Math.Min(100, Math.Max(0, SimulatePacketLossChance));
            SimulateMinLatency = Math.Max(0, SimulateMinLatency);
            SimulateMaxLatency = Math.Max(SimulateMinLatency, SimulateMaxLatency);
        }

        void Update()
        {
            m_NetManager?.PollEvents();
        }

        public override bool IsSupported => Application.platform != RuntimePlatform.WebGLPlayer;

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery qos)
        {
            if (!m_Peers.ContainsKey(clientId)) return;
            m_Peers[clientId].Send(data.Array, data.Offset, data.Count, ConvertNetworkDelivery(qos));
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            // transport is event based ignore this.
            clientId = 0;
            receiveTime = Time.realtimeSinceStartup;
            payload = new ArraySegment<Byte>();
            return NetworkEvent.Nothing;
        }

        public override bool StartClient()
        {
            if (m_HostType != HostType.None)
            {
                throw new InvalidOperationException("Already started as " + m_HostType);
            }

            m_HostType = HostType.Client;

            var success = m_NetManager.Start();
            if (success == false)
            {
                return false;
            }
            
            NetPeer peer = m_NetManager.Connect(Address, Port, string.Empty);

            if (peer.Id != 0)
            {
                throw new InvalidPacketException("Server peer did not have id 0: " + peer.Id);
            }

            m_Peers[(ulong)peer.Id] = peer;

            return true;
        }

        public override bool StartServer()
        {
            if (m_HostType != HostType.None)
            {
                throw new InvalidOperationException("Already started as " + m_HostType);
            }

            m_HostType = HostType.Server;

            bool success = m_NetManager.Start(Port);

            return success;
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (m_Peers.ContainsKey(clientId))
            {
                m_Peers[clientId].Disconnect();
            }
        }

        public override void DisconnectLocalClient()
        {
            m_NetManager.Flush();
            m_NetManager.DisconnectAll();
            m_Peers.Clear();
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            if (!m_Peers.ContainsKey(clientId))
            {
                return 0;
            }

            return (ulong)m_Peers[clientId].Ping * 2;
        }

        public override void Shutdown()
        {
            if (m_NetManager != null)
            {
                m_NetManager.Flush();
                m_NetManager.Stop();
            }

            m_Peers.Clear();

            m_HostType = HostType.None;
        }

        public override void Initialize()
        {
            m_MessageBuffer = new byte[MessageBufferSize];

            m_NetManager = new NetManager(this)
            {
                PingInterval = SecondsToMilliseconds(PingInterval),
                DisconnectTimeout = SecondsToMilliseconds(DisconnectTimeout),
                ReconnectDelay = SecondsToMilliseconds(ReconnectDelay),
                MaxConnectAttempts = MaxConnectAttempts,
                SimulatePacketLoss = SimulatePacketLossChance > 0,
                SimulationPacketLossChance = SimulatePacketLossChance,
                SimulateLatency = SimulateMaxLatency > 0,
                SimulationMinLatency = SimulateMinLatency,
                SimulationMaxLatency = SimulateMaxLatency
            };
        }

        DeliveryMethod ConvertNetworkDelivery(NetworkDelivery type)
        {
            switch (type)
            {
                case NetworkDelivery.Unreliable:
                    {
                        return DeliveryMethod.Unreliable;
                    }
                case NetworkDelivery.UnreliableSequenced:
                    {
                        return DeliveryMethod.Sequenced;
                    }
                case NetworkDelivery.Reliable:
                    {
                        return DeliveryMethod.ReliableUnordered;
                    }
                case NetworkDelivery.ReliableSequenced:
                    {
                        return DeliveryMethod.ReliableOrdered;
                    }
                case NetworkDelivery.ReliableFragmentedSequenced:
                    {
                        return DeliveryMethod.ReliableOrdered;
                    }
                default:
                    {
                        throw new ArgumentOutOfRangeException(nameof(type), type, null);
                    }
            }
        }

        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            var peerId = GetMlapiClientId(peer);
            InvokeOnTransportEvent(NetworkEvent.Connect, peerId, default, Time.time);

            m_Peers[peerId] = peer;
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            var peerId = GetMlapiClientId(peer);
            InvokeOnTransportEvent(NetworkEvent.Disconnect, GetMlapiClientId(peer), default, Time.time);

            m_Peers.Remove(peerId);
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            // Ignore
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod)
        {
            int size = reader.UserDataSize;
            if (size > m_MessageBuffer.Length)
            {
                ResizeMessageBuffer(size);
            }

            byte[] data = m_MessageBuffer;
            Buffer.BlockCopy(reader.RawData, reader.UserDataOffset, data, 0, size);

            // The last byte sent is used to indicate the channel so don't include it in the payload.
            var payload = new ArraySegment<byte>(data, 0, size - 1);

            InvokeOnTransportEvent(NetworkEvent.Data, GetMlapiClientId(peer), payload, Time.time);

            reader.Recycle();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void ResizeMessageBuffer(int size)
        {
            m_MessageBuffer = new byte[size];
            if (NetworkManager.Singleton.LogLevel == LogLevel.Developer)
            {
                NetworkLog.LogWarningServer($"LiteNetLibTransport resizing messageBuffer to size of {size}.");
            }
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
            // Ignore
        }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            // Ignore
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            request.Accept();
        }

        ulong GetMlapiClientId(NetPeer peer)
        {
            ulong clientId = (ulong)peer.Id;

            if (m_HostType == HostType.Server)
            {
                clientId += 1;
            }

            return clientId;
        }

        static int SecondsToMilliseconds(float seconds)
        {
            return (int)Mathf.Ceil(seconds * 1000);
        }
    }
}
