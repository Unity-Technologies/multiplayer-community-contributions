using System;
using System.Collections.Generic;
using System.Net;
using MLAPI.Transports;
using MLAPI.Transports.Tasks;
using Ruffles.Configuration;
using Ruffles.Connections;
using Ruffles.Core;
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

        [Header("SocketConfig")]
        public bool EnablePollEvents = true;
        public ushort EventQueueSize = 8192;
        public IPAddress IPv4ListenAddress = IPAddress.Any;
        public IPAddress IPv6ListenAddress = IPAddress.IPv6Any;
        public bool UseIPv6Dual = true;
        public bool AllowUnconnectedMessages = false;
        public ushort SocketPollTime = 50;
        public bool ReuseConnections = true;
        public ushort MaxMergeMessageSize = 1024;
        public ulong MaxMergeDelay = 100;
        public bool EnableMergedAcks = true;
        public byte MergedAckBytes = 8;
        public ushort MaximumMTU = 4096;
        public ushort MinimumMTU = 512;
        public bool EnablePathMTU = true;
        public byte MaxMTUAttempts = 8;
        public ulong MTUAttemptDelay = 1000;
        public double MTUGrowthFactor = 1.25;
        public ushort MaxFragments = 512;
        public ushort MaxBufferSize = 5120;
        public ushort MaxConnections = ushort.MaxValue;
        public ulong HandshakeTimeout = 30000;
        public ulong ConnectionTimeout = 30000;
        public ulong ConnectionRequestTimeout = 5000;
        public ulong HeartbeatDelay = 20000;
        public double MaxPacketLossPercentage = 0.8;
        public uint MaxRoundtripTime = 1500;
        public ulong ConnectionQualityGracePeriod = 5000;
        public ulong HandshakeResendDelay = 500;
        public byte MaxHandshakeResends = 20;
        public ulong ConnectionRequestMinResendDelay = 200;
        public byte MaxConnectionRequestResends = 5;
        public byte ChallengeDifficulty = 20;
        public uint ConnectionChallengeHistory = 2048;
        public ulong ConnectionChallengeTimeWindow = 300;
        public bool TimeBasedConnectionChallenge = true;
        public ushort MaxPendingConnections = ushort.MaxValue;
        public ushort AmplificationPreventionHandshakePadding = 512;
        public ushort ReliabilityWindowSize = 512;
        public ushort ReliableAckFlowWindowSize = 1024;
        public ulong ReliabilityMaxResendAttempts = 30;
        public double ReliabilityResendRoundtripMultiplier = 1.2;

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
        private readonly Dictionary<ulong, Ruffles.Connections.Connection> connections = new Dictionary<ulong, Ruffles.Connections.Connection>();
        private readonly Dictionary<string, byte> channelNameToId = new Dictionary<string, byte>();
        private readonly Dictionary<byte, string> channelIdToName = new Dictionary<byte, string>();
        private Connection serverConnection;

        // Ruffles
        private RuffleSocket socket;

        // Connector task
        private SocketTask connectTask;
        private Connection connectConnection;

        public override ulong ServerClientId => GetMLAPIClientId(0, true);

        public override void Send(ulong clientId, ArraySegment<byte> data, string channelName)
        {
            GetRufflesConnectionDetails(clientId, out ulong connectionId);

            byte channelId = channelNameToId[channelName];

            socket.Send(data, connectionId, channelId, false);
        }

        public override NetEventType PollEvent(out ulong clientId, out string channelName, out ArraySegment<byte> payload, out float receiveTime)
        {
            NetworkEvent @event = socket.Poll();

            receiveTime = Time.realtimeSinceStartup - (float)(DateTime.Now - @event.SocketReceiveTime).TotalSeconds;

            if (@event.Type != NetworkEventType.Nothing)
            {
                clientId = GetMLAPIClientId(@event.Connection.Id, false);
            }
            else
            {
                clientId = 0;
            }

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

            // Translate NetworkEventType to NetEventType
            switch (@event.Type)
            {
                case NetworkEventType.Data:
                    @event.Recycle();
                    return NetEventType.Data;
                case NetworkEventType.Connect:
                    {
                        if (isConnector && @event.Connection == connectConnection && connectTask != null)
                        {
                            // Connection failed
                            connectTask.Success = true;
                            connectTask.IsDone = true;

                            connectTask = null;
                        }

                        connections.Add(@event.Connection.Id, @event.Connection);

                        // Set the server connectionId
                        if (isConnector)
                        {
                            serverConnection = @event.Connection;
                        }

                        @event.Recycle();

                        return NetEventType.Connect;
                    }
                case NetworkEventType.Timeout:
                case NetworkEventType.Disconnect:
                    {
                        if (isConnector && @event.Connection == connectConnection && connectTask != null)
                        {
                            // Connection failed
                            connectTask.Success = false;
                            connectTask.IsDone = true;
                            connectTask = null;
                        }

                        if (@event.Connection == serverConnection)
                        {
                            serverConnection = null;
                        }

                        connections.Remove(@event.Connection.Id);

                        @event.Recycle();

                        return NetEventType.Disconnect;
                    }
                case NetworkEventType.Nothing:
                    @event.Recycle();
                    return NetEventType.Nothing;
            }

            return NetEventType.Nothing;
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

            connectConnection = socket.Connect(new IPEndPoint(IPAddress.Parse(ConnectAddress), Port));

            if (connectConnection == null)
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
            socket.Disconnect(connections[connectionId], true);
        }

        public override void DisconnectLocalClient()
        {
            socket.Disconnect(serverConnection, true);
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
            socket.Shutdown();
        }

        public override void Init()
        {
            messageBuffer = new byte[TransportBufferSize];
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
                connectionId = (ushort)(clientId - 1);
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
                ConnectionQualityGracePeriod = ConnectionQualityGracePeriod,
                ConnectionRequestMinResendDelay = ConnectionRequestMinResendDelay,
                ConnectionTimeout = ConnectionTimeout,
                DualListenPort = server ? Port : (ushort)0,
                EnableChannelUpdates = EnableChannelUpdates,
                EnableConnectionRequestResends = EnableConnectionRequestResends,
                EnableCallbackEvents = false,
                EnableHeartbeats = EnableHeartbeats,
                EnableMergedAcks = EnableMergedAcks,
                EnablePacketMerging = EnablePacketMerging,
                EnablePathMTU = EnablePathMTU,
                EnablePollEvents = true,
                EnableTimeouts = EnableTimeouts,
                EventQueueSize = EventQueueSize,
                HandshakeResendDelay = HandshakeResendDelay,
                HandshakeTimeout = HandshakeTimeout,
                HeartbeatDelay = HeartbeatDelay,
                IPv4ListenAddress = IPAddress.Any,
                IPv6ListenAddress = IPAddress.IPv6Any,
                MaxBufferSize = MaxBufferSize,
                MaxConnectionRequestResends = MaxConnectionRequestResends,
                MaxConnections = MaxConnections,
                MaxFragments = MaxFragments,
                MaxHandshakeResends = MaxHandshakeResends,
                MaximumMTU = MaximumMTU,
                MaxMergeDelay = MaxMergeDelay,
                MaxMergeMessageSize = MaxMergeMessageSize,
                MaxMTUAttempts = MaxMTUAttempts,
                MaxPacketLossPercentage = MaxPacketLossPercentage,
                MaxPendingConnections = MaxPendingConnections,
                MaxRoundtripTime = MaxRoundtripTime,
                MergedAckBytes = MergedAckBytes,
                MinimumMTU = MinimumMTU,
                MTUAttemptDelay = MTUAttemptDelay,
                MTUGrowthFactor = MTUGrowthFactor,
                ReliabilityMaxResendAttempts = ReliabilityMaxResendAttempts,
                ReliabilityResendRoundtripMultiplier = ReliabilityResendRoundtripMultiplier,
                ReliabilityWindowSize = ReliableAckFlowWindowSize,
                ReliableAckFlowWindowSize = ReliableAckFlowWindowSize,
                ReuseConnections = ReuseConnections,
                SimulatorConfig = new Ruffles.Simulation.SimulatorConfig()
                {
                    DropPercentage = DropPercentage,
                    MaxLatency = MaxLatency,
                    MinLatency = MinLatency
                },
                SocketPollTime = SocketPollTime,
                TimeBasedConnectionChallenge = TimeBasedConnectionChallenge,
                UseIPv6Dual = UseIPv6Dual,
                UseSimulator = UseSimulator,
                ConnectionRequestTimeout = ConnectionRequestTimeout
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
