using System;
using System.Collections.Generic;
using System.Net;
using Ruffles.Configuration;
using Ruffles.Core;
using Ruffles.Time;
using Ruffles.Channeling;
using Ruffles.Simulation;
using Unity.Netcode;
using UnityEngine;
using LogLevel = Ruffles.Utils.LogLevel;
using NetworkEvent = Unity.Netcode.NetworkEvent;
using RufflesConnection = Ruffles.Connections.Connection;
using RufflesNetworkEvent = Ruffles.Core.NetworkEvent;
using RufflesLogging = Ruffles.Utils.Logging;

namespace Netcode.Transports.Ruffles
{
    public class RufflesTransport : NetworkTransport
    {
        public override bool IsSupported => Application.platform != RuntimePlatform.WebGLPlayer;

        // Inspector / settings
        [Header("Transport")]
        public string ConnectAddress = "127.0.0.1";
        public ushort Port = 7777;
        public int TransportBufferSize = 1024 * 8;
        public LogLevel LogLevel = LogLevel.Info;

        [Header("SocketConfig")]
        public bool EnableSyncronizationEvent = false;
        public bool EnableSyncronizedCallbacks = false;
        public int EventQueueSize = 1024 * 8;
        public int ProcessingQueueSize = 1024 * 8;
        public int HeapPointersPoolSize = 1024;
        public int HeapMemoryPoolSize = 1024;
        public int MemoryWrapperPoolSize = 1024;
        public int ChannelPoolSize = 1024;
        public PooledChannelType PooledChannels = PooledChannelType.All;
        public IPAddress IPv4ListenAddress = IPAddress.Any;
        public IPAddress IPv6ListenAddress = IPAddress.IPv6Any;
        public bool UseIPv6Dual = true;
        public bool AllowUnconnectedMessages = false;
        public bool AllowBroadcasts = false;
        public bool EnableAckNotifications = false;
        public int LogicDelay = 50;
        public bool ReuseChannels = true;
        public int LogicThreads = 1;
        public int SocketThreads = 1;
        public int ProcessingThreads = 0;
        public int MaxMergeMessageSize = 1024;
        public int MaxMergeDelay = 15;
        public bool EnableMergedAcks = true;
        public int MergedAckBytes = 8;
        public int MaximumMTU = 4096;
        public int MinimumMTU = 512;
        public bool EnablePathMTU = true;
        public int MaxMTUAttempts = 8;
        public int MTUAttemptDelay = 1000;
        public double MTUGrowthFactor = 1.25;
        public int MaxFragments = 512;
        public int MaxBufferSize = 1024 * 5;
        public int HandshakeTimeout = 20_000;
        public int ConnectionTimeout = 20_000;
        public int HeartbeatDelay = 5000;
        public int HandshakeResendDelay = 500;
        public int MaxHandshakeResends = 20;
        public int ConnectionRequestMinResendDelay = 500;
        public int MaxConnectionRequestResends = 5;
        public int ConnectionRequestTimeout = 5000;
        public int ChallengeDifficulty = 20;
        public int ConnectionChallengeHistory = 2048;
        public int ConnectionChallengeTimeWindow = 60 * 5;
        public bool TimeBasedConnectionChallenge = true;
        public int AmplificationPreventionHandshakePadding = 512;
        public int ReliabilityWindowSize = 512;
        public int ReliableAckFlowWindowSize = 1024;
        public int ReliabilityMaxResendAttempts = 30;
        public double ReliabilityResendRoundtripMultiplier = 1.2;
        public int ReliabilityMinPacketResendDelay = 100;
        public int ReliabilityMinAckResendDelay = 100;

        [Header("Simulator")]
        public bool UseSimulator = false;
        public float DropPercentage = 0.1f;
        public int MaxLatency = 100;
        public int MinLatency = 30;

        [Header("Danger")]
        public bool EnableHeartbeats = true;
        public bool EnableTimeouts = true;
        public bool EnableChannelUpdates = true;
        public bool EnableConnectionRequestResends = true;
        public bool EnablePacketMerging = true;

        // Runtime / state
        private byte[] messageBuffer;
        private WeakReference temporaryBufferReference;
        private bool isConnector = false;

        // Lookup / translation
        private readonly Dictionary<ulong, RufflesConnection> connections = new Dictionary<ulong, RufflesConnection>();

        private readonly Dictionary<NetworkDelivery, byte> channelNameToId = new Dictionary<NetworkDelivery, byte>();

        // Ruffles
        private RuffleSocket socket;

        // Connector task
        private RufflesConnection serverConnection;

        public override ulong ServerClientId => GetMLAPIClientId(0, true);

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            GetRufflesConnectionDetails(clientId, out ulong connectionId);

            byte channelId = channelNameToId[delivery];

            connections[connectionId].Send(data, channelId, false, 0);
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            RufflesNetworkEvent @event = socket.Poll();

            receiveTime = Time.realtimeSinceStartup - (float)(NetTime.Now - @event.SocketReceiveTime).TotalSeconds;

            byte[] dataBuffer = messageBuffer;

            if (@event.Type == NetworkEventType.Data)
            {
                if (@event.Data.Count > messageBuffer.Length)
                {
                    if (temporaryBufferReference != null && temporaryBufferReference.IsAlive && ((byte[])temporaryBufferReference.Target).Length >= @event.Data.Count)
                    {
                        dataBuffer = (byte[])temporaryBufferReference.Target;
                    }
                    else
                    {
                        dataBuffer = new byte[@event.Data.Count];
                        temporaryBufferReference = new WeakReference(dataBuffer);
                    }
                }

                Buffer.BlockCopy(@event.Data.Array, @event.Data.Offset, dataBuffer, 0, @event.Data.Count);
                payload = new ArraySegment<byte>(dataBuffer, 0, @event.Data.Count);
            }
            else
            {
                payload = new ArraySegment<byte>();
            }

            if (@event.Connection != null)
            {
                clientId = GetMLAPIClientId(@event.Connection.Id, false);
            }
            else
            {
                clientId = 0;
            }

            // Translate NetworkEventType to NetEventType
            switch (@event.Type)
            {
                case NetworkEventType.Data:
                    @event.Recycle();
                    return NetworkEvent.Data;
                case NetworkEventType.Connect:
                {
                    if (isConnector && @event.Connection == serverConnection)
                    {
                        // Connection successful
                    }

                    // Add the connection
                    connections.Add(@event.Connection.Id, @event.Connection);

                    @event.Recycle();
                    return NetworkEvent.Connect;
                }
                case NetworkEventType.Timeout:
                case NetworkEventType.Disconnect:
                {
                    if (isConnector && @event.Connection == serverConnection)
                    {
                        // Connection failed
                    }

                    connections.Remove(@event.Connection.Id);

                    @event.Recycle();
                    return NetworkEvent.Disconnect;
                }
                default:
                    @event.Recycle();
                    return NetworkEvent.Nothing;
            }
        }

        public override bool StartClient()
        {
            SocketConfig config = GetConfig(false);

            socket = new RuffleSocket(config);

            isConnector = true;

            if (!socket.Start())
            {
                return false;
            }

            serverConnection = socket.Connect(new IPEndPoint(IPAddress.Parse(ConnectAddress), Port));

            if (serverConnection == null)
            {
                return false;
            }

            return true;
        }

        public override bool StartServer()
        {
            SocketConfig config = GetConfig(true);

            socket = new RuffleSocket(config);

            serverConnection = null;
            isConnector = false;

            return socket.Start();
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            GetRufflesConnectionDetails(clientId, out ulong connectionId);

            connections[connectionId].Disconnect(true);
        }

        public override void DisconnectLocalClient()
        {
            if (serverConnection != null)
            {
                serverConnection.Disconnect(true);
            }
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            GetRufflesConnectionDetails(clientId, out ulong connectionId);

            return (ulong)connections[connectionId].Roundtrip;
        }

        public override void Shutdown()
        {
            channelNameToId.Clear();
            connections.Clear();

            // Release to GC
            messageBuffer = null;

            if (socket != null && socket.IsInitialized)
            {
                // Releases memory and other things
                socket.Shutdown();
            }

            // Release to GC
            socket = null;

            // Release server connection to GC
            serverConnection = null;
        }

        public override void Initialize(NetworkManager networkManager = null)
        {
            messageBuffer = new byte[TransportBufferSize];

            RufflesLogging.CurrentLogLevel = LogLevel;
        }

        public ulong GetMLAPIClientId(ulong connectionId, bool isServer)
        {
            if (isServer)
            {
                return 0;
            }
            else
            {
                return connectionId + 1;
            }
        }

        public void GetRufflesConnectionDetails(ulong clientId, out ulong connectionId)
        {
            if (clientId == 0)
            {
                connectionId = serverConnection.Id;
            }
            else
            {
                connectionId = clientId - 1;
            }
        }

        private SocketConfig GetConfig(bool server)
        {
            SocketConfig config = new SocketConfig()
            {
                AllowUnconnectedMessages = AllowUnconnectedMessages,
                AmplificationPreventionHandshakePadding = AmplificationPreventionHandshakePadding,
                ChallengeDifficulty = ChallengeDifficulty,
                ConnectionChallengeHistory = ConnectionChallengeHistory,
                ChannelTypes = null,
                ConnectionChallengeTimeWindow = ConnectionChallengeTimeWindow,
                ConnectionRequestMinResendDelay = ConnectionRequestMinResendDelay,
                ConnectionTimeout = ConnectionTimeout,
                DualListenPort = server ? Port : (ushort)0,
                EnableChannelUpdates = EnableChannelUpdates,
                EnableConnectionRequestResends = EnableConnectionRequestResends,
                EnableHeartbeats = EnableHeartbeats,
                EnableMergedAcks = EnableMergedAcks,
                EnablePacketMerging = EnablePacketMerging,
                EnablePathMTU = EnablePathMTU,
                EnableTimeouts = EnableTimeouts,
                EventQueueSize = EventQueueSize,
                HandshakeResendDelay = HandshakeResendDelay,
                HandshakeTimeout = HandshakeTimeout,
                HeartbeatDelay = HeartbeatDelay,
                IPv4ListenAddress = IPv4ListenAddress,
                IPv6ListenAddress = IPv6ListenAddress,
                MaxBufferSize = MaxBufferSize,
                MaxConnectionRequestResends = MaxConnectionRequestResends,
                MaxFragments = MaxFragments,
                MaxHandshakeResends = MaxHandshakeResends,
                MaximumMTU = MaximumMTU,
                MaxMergeDelay = MaxMergeDelay,
                MaxMergeMessageSize = MaxMergeMessageSize,
                MaxMTUAttempts = MaxMTUAttempts,
                MergedAckBytes = MergedAckBytes,
                MinimumMTU = MinimumMTU,
                MTUAttemptDelay = MTUAttemptDelay,
                MTUGrowthFactor = MTUGrowthFactor,
                ReliabilityMaxResendAttempts = ReliabilityMaxResendAttempts,
                ReliabilityResendRoundtripMultiplier = ReliabilityResendRoundtripMultiplier,
                ReliabilityWindowSize = ReliableAckFlowWindowSize,
                ReliableAckFlowWindowSize = ReliableAckFlowWindowSize,
                SimulatorConfig = new SimulatorConfig()
                {
                    DropPercentage = DropPercentage,
                    MaxLatency = MaxLatency,
                    MinLatency = MinLatency
                },
                TimeBasedConnectionChallenge = TimeBasedConnectionChallenge,
                UseIPv6Dual = UseIPv6Dual,
                UseSimulator = UseSimulator,
                ConnectionRequestTimeout = ConnectionRequestTimeout,
                ChannelPoolSize = ChannelPoolSize,
                HeapMemoryPoolSize = HeapMemoryPoolSize,
                HeapPointersPoolSize = HeapPointersPoolSize,
                MemoryWrapperPoolSize = MemoryWrapperPoolSize,
                PooledChannels = PooledChannels,
                ReliabilityMinAckResendDelay = ReliabilityMinAckResendDelay,
                ReliabilityMinPacketResendDelay = ReliabilityMinPacketResendDelay,
                AllowBroadcasts = AllowBroadcasts,
                EnableSyncronizationEvent = EnableSyncronizationEvent,
                EnableSyncronizedCallbacks = EnableSyncronizedCallbacks,
                LogicDelay = LogicDelay,
                LogicThreads = LogicThreads,
                ProcessingQueueSize = ProcessingQueueSize,
                ProcessingThreads = ProcessingThreads,
                ReuseChannels = ReuseChannels,
                SocketThreads = SocketThreads,
                EnableAckNotifications = EnableAckNotifications
            };

            var deliveryValues = Enum.GetValues(typeof(NetworkDelivery));
            config.ChannelTypes = new ChannelType[deliveryValues.Length];

            for (byte i = 0; i < deliveryValues.Length; i++)
            {
                var delivery = (NetworkDelivery)deliveryValues.GetValue(i);
                config.ChannelTypes[i] = ConvertChannelType(delivery);
                channelNameToId.Add(delivery, i);
            }

            return config;
        }

        private ChannelType ConvertChannelType(NetworkDelivery type)
        {
            switch (type)
            {
                case NetworkDelivery.Reliable:
                    return ChannelType.Reliable;
                case NetworkDelivery.ReliableFragmentedSequenced:
                    return ChannelType.ReliableSequencedFragmented;
                case NetworkDelivery.ReliableSequenced:
                    return ChannelType.ReliableSequenced;
                case NetworkDelivery.Unreliable:
                    return ChannelType.Unreliable;
                case NetworkDelivery.UnreliableSequenced:
                    return ChannelType.UnreliableOrdered;
            }

            return ChannelType.Reliable;
        }
    }
}
