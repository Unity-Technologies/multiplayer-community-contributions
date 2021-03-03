using System;
using System.Collections.Generic;
using System.Net;
using MLAPI.Transports;
using MLAPI.Transports.Tasks;
using Ruffles.Configuration;
using Ruffles.Connections;
using Ruffles.Core;
using Ruffles.Time;
using Ruffles.Utils;
using UnityEngine;

namespace RufflesTransport
{
    public class RufflesTransport : Transport
    {
        [Serializable]
        public class RufflesChannel
        {
            public string Name;
            public Ruffles.Channeling.ChannelType Type;
        }

        public override bool IsSupported => Application.platform != RuntimePlatform.WebGLPlayer;

        // Inspector / settings
        [Header("Transport")]
        public string ConnectAddress = "127.0.0.1";
        public ushort Port = 7777;
        public List<RufflesChannel> Channels = new List<RufflesChannel>();
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
        public Ruffles.Channeling.PooledChannelType PooledChannels = Ruffles.Channeling.PooledChannelType.All;
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
        private readonly Dictionary<ulong, Connection> connections = new Dictionary<ulong, Connection>();

        private readonly Dictionary<string, byte> channelNameToId = new Dictionary<string, byte>();
        private readonly Dictionary<byte, string> channelIdToName = new Dictionary<byte, string>();

        // Ruffles
        private RuffleSocket socket;

        // Connector task
        private SocketTask connectTask;
        private Connection serverConnection;

        public override ulong ServerClientId => GetMLAPIClientId(0, true);

        public override void Send(ulong clientId, ArraySegment<byte> data, string channelName)
        {
            GetRufflesConnectionDetails(clientId, out ulong connectionId);

            byte channelId = channelNameToId[channelName];

            connections[connectionId].Send(data, channelId, false, 0);
        }

        public override NetEventType PollEvent(out ulong clientId, out string channelName, out ArraySegment<byte> payload, out float receiveTime)
        {
            NetworkEvent @event = socket.Poll();

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

            channelName = channelIdToName[@event.ChannelId];

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
                    return NetEventType.Data;
                case NetworkEventType.Connect:
                    {
                        if (isConnector && @event.Connection == serverConnection && connectTask != null)
                        {
                            // Connection successful
                            connectTask.Success = true;
                            connectTask.IsDone = true;
                            connectTask = null;
                        }

                        // Add the connection
                        connections.Add(@event.Connection.Id, @event.Connection);

                        @event.Recycle();
                        return NetEventType.Connect;
                    }
                case NetworkEventType.Timeout:
                case NetworkEventType.Disconnect:
                    {
                        if (isConnector && @event.Connection == serverConnection && connectTask != null)
                        {
                            // Connection failed
                            connectTask.Success = false;
                            connectTask.IsDone = true;
                            connectTask = null;
                        }

                        connections.Remove(@event.Connection.Id);

                        @event.Recycle();
                        return NetEventType.Disconnect;
                    }
                default:
                    @event.Recycle();
                    return NetEventType.Nothing;
            }
        }

        public override SocketTasks StartClient()
        {
            SocketConfig config = GetConfig(false);

            socket = new RuffleSocket(config);

            isConnector = true;

            if (!socket.Start())
            {
                return SocketTask.Fault.AsTasks();
            }

            serverConnection = socket.Connect(new IPEndPoint(IPAddress.Parse(ConnectAddress), Port));

            if (serverConnection == null)
            {
                return SocketTask.Fault.AsTasks();
            }
            else
            {
                connectTask = SocketTask.Working;

                return connectTask.AsTasks();
            }
        }

        public override SocketTasks StartServer()
        {
            SocketConfig config = GetConfig(true);

            socket = new RuffleSocket(config);

            serverConnection = null;
            isConnector = false;

            if (socket.Start())
            {
                return SocketTask.Done.AsTasks();
            }
            else
            {
                return SocketTask.Fault.AsTasks();
            }
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
            channelIdToName.Clear();
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

            // Null the connect task
            connectTask = null;
        }

        public override void Init()
        {
            messageBuffer = new byte[TransportBufferSize];

            Logging.CurrentLogLevel = LogLevel;
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
                SimulatorConfig = new Ruffles.Simulation.SimulatorConfig()
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

            int channelCount = MLAPI_CHANNELS.Length + Channels.Count;
            config.ChannelTypes = new Ruffles.Channeling.ChannelType[channelCount];

            for (byte i = 0; i < MLAPI_CHANNELS.Length; i++)
            {
                config.ChannelTypes[i] = ConvertChannelType(MLAPI_CHANNELS[i].Type);
                channelIdToName.Add(i, MLAPI_CHANNELS[i].Name);
                channelNameToId.Add(MLAPI_CHANNELS[i].Name, i);
            }

            for (byte i = (byte)MLAPI_CHANNELS.Length; i < Channels.Count + MLAPI_CHANNELS.Length; i++)
            {
                config.ChannelTypes[i] = Channels[config.ChannelTypes.Length - 1 - i].Type;
                channelIdToName.Add(i, Channels[config.ChannelTypes.Length - 1 - i].Name);
                channelNameToId.Add(Channels[config.ChannelTypes.Length - 1 - i].Name, i);
            }

            return config;
        }

        private Ruffles.Channeling.ChannelType ConvertChannelType(ChannelType type)
        {
            switch (type)
            {
                case ChannelType.Reliable:
                    return Ruffles.Channeling.ChannelType.Reliable;
                case ChannelType.ReliableFragmentedSequenced:
                    return Ruffles.Channeling.ChannelType.ReliableSequencedFragmented;
                case ChannelType.ReliableSequenced:
                    return Ruffles.Channeling.ChannelType.ReliableSequenced;
                case ChannelType.Unreliable:
                    return Ruffles.Channeling.ChannelType.Unreliable;
                case ChannelType.UnreliableSequenced:
                    return Ruffles.Channeling.ChannelType.UnreliableOrdered;
            }

            return Ruffles.Channeling.ChannelType.Reliable;
        }
    }
}
