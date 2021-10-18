using System;
using System.Collections.Generic;
using ENet;
using Unity.Netcode;
using Unity.Profiling;
using UnityEngine;
using Event = ENet.Event;
using EventType = ENet.EventType;

namespace Netcode.Transports.Enet
{
    [DefaultExecutionOrder(1000)]
    public class EnetTransport : NetworkTransport
    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        static ProfilerMarker s_PollEvent =
            new ProfilerMarker("Enet.PollEvent");
        static ProfilerMarker s_Service =
            new ProfilerMarker("Enet.Service");
        static ProfilerMarker s_Connect =
            new ProfilerMarker("Enet.Connect");
        static ProfilerMarker s_Disconnect =
            new ProfilerMarker("Enet.Disconnect");
        static ProfilerMarker s_Receive =
            new ProfilerMarker("Enet.Receive");
        static ProfilerMarker s_Timeout =
            new ProfilerMarker("Enet.Timeout");
        static ProfilerMarker s_NoEvent =
            new ProfilerMarker("Enet.NoEvent");
        static ProfilerMarker s_Flush =
            new ProfilerMarker("Enet.Flush");
#endif
        

        public override bool IsSupported => Application.platform != RuntimePlatform.WebGLPlayer;

        public ushort Port = 7777;
        public string Address = "127.0.0.1";
        public int MaxClients = 100;
        public int MessageBufferSize = 1024 * 5;

        [Header("ENET Settings")]
        public uint PingInterval = 500;
        public uint TimeoutLimit = 32;
        public uint TimeoutMinimum = 5000;
        public uint TimeoutMaximum = 30000;


        // Runtime / state
        private byte[] messageBuffer;
        private WeakReference temporaryBufferReference;
        
        private readonly Dictionary<uint, Peer> connectedEnetPeers = new Dictionary<uint, Peer>();

        private Host host;

        private uint serverPeerId;

        private bool hasServiced;

        public override ulong ServerClientId => GetMLAPIClientId(0, true);

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            Packet packet = default;

            packet.Create(data.Array, data.Offset, data.Count, NetworkDeliveryToPacketFlag(delivery));

            GetEnetConnectionDetails(clientId, out uint peerId);

            connectedEnetPeers[peerId].Send(0, ref packet);
        }

        public void Update()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            s_Flush.Begin();
#endif
            host?.Flush();
            hasServiced = false;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            s_Flush.End();
#endif
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            s_PollEvent.Begin();
#endif
            try
            {
                Event @event;

                if (host.CheckEvents(out @event) <= 0)
                {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    s_Service.Begin();
#endif
                    try
                    {
                        if (hasServiced || host.Service(0, out @event) <= 0)
                        {
                            clientId = 0;
                            payload = new ArraySegment<byte>();
                            receiveTime = Time.realtimeSinceStartup;

                            return NetworkEvent.Nothing;
                        }
                        hasServiced = true;
                    }
                    finally
                    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        s_Service.End();
#endif
                    }

                }

                clientId = GetMLAPIClientId(@event.Peer.ID, false);

                switch (@event.Type)
                {
                    case EventType.Connect:
                    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        s_Connect.Begin();
#endif
                        payload = new ArraySegment<byte>();
                        receiveTime = Time.realtimeSinceStartup;

                        connectedEnetPeers.Add(@event.Peer.ID, @event.Peer);

                        @event.Peer.PingInterval(PingInterval);
                        @event.Peer.Timeout(TimeoutLimit, TimeoutMinimum, TimeoutMaximum);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        s_Connect.End();
#endif
                        return NetworkEvent.Connect;
                    }
                    case EventType.Disconnect:
                    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        s_Disconnect.Begin();
#endif
                        payload = new ArraySegment<byte>();
                        receiveTime = Time.realtimeSinceStartup;

                        connectedEnetPeers.Remove(@event.Peer.ID);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        s_Disconnect.End();
#endif
                        return NetworkEvent.Disconnect;
                    }
                    case EventType.Receive:
                    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        s_Receive.Begin();
#endif
                        receiveTime = Time.realtimeSinceStartup;
                        int size = @event.Packet.Length;

                        if (size > messageBuffer.Length)
                        {
                            byte[] tempBuffer;

                            if (temporaryBufferReference != null && temporaryBufferReference.IsAlive && ((byte[])temporaryBufferReference.Target).Length >= size)
                            {
                                tempBuffer = (byte[])temporaryBufferReference.Target;
                            }
                            else
                            {
                                tempBuffer = new byte[size];
                                temporaryBufferReference = new WeakReference(tempBuffer);
                            }

                            @event.Packet.CopyTo(tempBuffer);
                            payload = new ArraySegment<byte>(tempBuffer, 0, size);
                        }
                        else
                        {
                            @event.Packet.CopyTo(messageBuffer);
                            payload = new ArraySegment<byte>(messageBuffer, 0, size);
                        }

                        @event.Packet.Dispose();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        s_Receive.End();
#endif
                        return NetworkEvent.Data;
                    }
                    case EventType.Timeout:
                    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        s_Timeout.Begin();
#endif
                        payload = new ArraySegment<byte>();
                        receiveTime = Time.realtimeSinceStartup;

                        connectedEnetPeers.Remove(@event.Peer.ID);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        s_Timeout.End();
#endif
                        return NetworkEvent.Disconnect;
                    }
                    case EventType.None:
                    default:
                    {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        s_NoEvent.Begin();
#endif
                        payload = new ArraySegment<byte>();
                        receiveTime = Time.realtimeSinceStartup;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        s_NoEvent.End();
#endif
                        return NetworkEvent.Nothing;
                    }
                }
            }
            finally
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                s_PollEvent.End();
#endif
            }
        }

        public override bool StartClient()
        {
            host = new Host();

            host.Create(1, 16);

            Address address = new Address();
            address.Port = Port;
            address.SetHost(Address);

            Peer serverPeer = host.Connect(address, 1); // Currently Netcode for GameObjects does not use transport level channels.

            serverPeer.PingInterval(PingInterval);
            serverPeer.Timeout(TimeoutLimit, TimeoutMinimum, TimeoutMaximum);

            serverPeerId = serverPeer.ID;

            return true;
        }

        public override bool StartServer()
        {
            host = new Host();

            Address address = new Address();
            address.Port = Port;

            host.Create(address, MaxClients, 1); // Currently Netcode for GameObjects does not use transport level channels.

            return true;
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            GetEnetConnectionDetails(serverPeerId, out uint peerId);

            connectedEnetPeers[peerId].DisconnectNow(0);
        }

        public override void DisconnectLocalClient()
        {
            host.Flush();

            GetEnetConnectionDetails(serverPeerId, out uint peerId);

            if (connectedEnetPeers.ContainsKey(peerId))
            {
                connectedEnetPeers[peerId].DisconnectNow(0);
            }
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            GetEnetConnectionDetails(clientId, out uint peerId);

            return connectedEnetPeers[peerId].RoundTripTime;
        }

        public override void Shutdown()
        {
            if (host != null)
            {
                host.Flush();
                host.Dispose();
                host = null;
            }
            
            connectedEnetPeers.Clear();

            Library.Deinitialize();
        }

        public override void Initialize()
        {
            Library.Initialize();

            connectedEnetPeers.Clear();
            
            messageBuffer = new byte[MessageBufferSize];
        }

        public PacketFlags NetworkDeliveryToPacketFlag(NetworkDelivery delivery)
        {
            return delivery switch
            {
                NetworkDelivery.Unreliable => PacketFlags.Unsequenced,
                NetworkDelivery.Reliable => PacketFlags.Reliable,  // ENET csharp Does not support ReliableUnsequenced. https://github.com/MidLevel/MLAPI.Transports/pull/5#issuecomment-498311723
                NetworkDelivery.ReliableSequenced => PacketFlags.Reliable,
                NetworkDelivery.ReliableFragmentedSequenced => PacketFlags.Reliable,
                NetworkDelivery.UnreliableSequenced => PacketFlags.None, // unreliable sequenced according to docs here https://github.com/nxrighthere/ENet-CSharp
                _ => throw new ArgumentOutOfRangeException(nameof(delivery), delivery, null)
            };
        }

        public ulong GetMLAPIClientId(uint peerId, bool isServer)
        {
            if (isServer)
            {
                return 0;
            }
            else
            {
                return peerId + 1;
            }
        }

        public void GetEnetConnectionDetails(ulong clientId, out uint peerId)
        {
            if (clientId == 0)
            {
                peerId = serverPeerId;
            }
            else
            {
                peerId = (uint)clientId - 1;
            }
        }
    }
}
