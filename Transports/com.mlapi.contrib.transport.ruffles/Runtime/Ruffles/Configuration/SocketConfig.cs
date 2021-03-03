using System;
using System.Collections.Generic;
using System.Net;
using Ruffles.BandwidthTracking;
using Ruffles.Channeling;
using Ruffles.Simulation;
using Ruffles.Utils;

namespace Ruffles.Configuration
{
    public class SocketConfig
    {
        // General
        /// <summary>
        /// Whether or not to enable the syncronization event to be triggered when a new event is added.
        /// </summary>
        public bool EnableSyncronizationEvent = true;
        /// <summary>
        /// Whether or not to enable invoking of syncronized callbacks when a new event is added.
        /// </summary>
        public bool EnableSyncronizedCallbacks = true;
        /// <summary>
        /// The size of the global event queue. 
        /// If this gets full no more events can be processed and the application will freeze until it is polled.
        /// </summary>
        public int EventQueueSize = 1024 * 8;
        /// <summary>
        /// The size of the processing queue.
        /// If this gets full. Packet processing will be stalled until the ProcessorThreads can catch up.
        /// </summary>
        public int ProcessingQueueSize = 1024 * 8;
        /// <summary>
        /// The pool size of the HeapPointers pool.
        /// </summary>
        public int HeapPointersPoolSize = 1024;
        /// <summary>
        /// The pool size of the HeapMemory pool.
        /// </summary>
        public int HeapMemoryPoolSize = 1024;
        /// <summary>
        /// The pool size of the MemoryWrapper pool.
        /// </summary>
        public int MemoryWrapperPoolSize = 1024;
        /// <summary>
        /// The pool size of every channel pool.
        /// </summary>
        public int ChannelPoolSize = 1024;
        /// <summary>
        /// The channels to pool.
        /// </summary>
        public PooledChannelType PooledChannels = PooledChannelType.All;

        // Connection
        /// <summary>
        /// The IPv4 address the socket will listen on.
        /// </summary>
        public IPAddress IPv4ListenAddress = IPAddress.Any;
        /// <summary>
        /// The IPv6 address the socket will listen on if UseDualIPv6 is turned on.
        /// </summary>
        public IPAddress IPv6ListenAddress = IPAddress.IPv6Any;
        /// <summary>
        /// The port that will be used to listen on both IPv4 and IPv6 if UseDualMode is turned on.
        /// </summary>
        public int DualListenPort = 0;
        /// <summary>
        /// Whether or not the socket will listen on IPv4 and IPv6 in dual mode on the same port.
        /// </summary>
        public bool UseIPv6Dual = true;
        /// <summary>
        /// Whether or not unconnected messages should be allowed.
        /// </summary>
        public bool AllowUnconnectedMessages = false;
        /// <summary>
        /// Whether or not broadcast messages should be allowed.
        /// </summary>
        public bool AllowBroadcasts = false;

        // Performance
        /// <summary>
        /// The max socket block time in milliseconds. This will affect how long the internal loop will block.
        /// </summary>
        public int LogicDelay = 50;
        /// <summary>
        /// Whether or not to reuse channels. Disabling this has an impact on memory and CPU.
        /// If this is enabled, all channels are automatically recycled when an connection dies.
        /// </summary>
        public bool ReuseChannels = true;
        /// <summary>
        /// The amount of logic threads to start.
        /// </summary>
        public int LogicThreads = 1;
        /// <summary>
        /// The amount of socket threads to start.
        /// </summary>
        public int SocketThreads = 1;
        /// <summary>
        /// The amount of packet processing threads to start.
        /// </summary>
        public int ProcessingThreads = 0;

        // Bandwidth
        /// <summary>
        /// The maximum size of a merged packet. 
        /// Increasing this increases the memory usage for each connection.
        /// </summary>
        public int MaxMergeMessageSize = 1024;
        /// <summary>
        /// The maximum delay before merged packets are sent.
        /// </summary>
        public int MaxMergeDelay = 100;
        /// <summary>
        /// Whether or not to enable merged acks for non fragmented channels.
        /// </summary>
        public bool EnableMergedAcks = true;
        /// <summary>
        /// The amount of bytes to use for merged acks.
        /// </summary>
        public int MergedAckBytes = 8;

        // Fragmentation
        /// <summary>
        /// The maximum MTU size that will be attempted using path MTU.
        /// </summary>
        public int MaximumMTU = 4096;
        /// <summary>
        /// The minimum MTU size. This is the default maximum packet size.
        /// </summary>
        public int MinimumMTU = 512;
        /// <summary>
        /// Whether or not to enable path MTU.
        /// </summary>
        public bool EnablePathMTU = true;
        /// <summary>
        /// The maximum amount of MTU requests to attempt.
        /// </summary>
        public int MaxMTUAttempts = 8;
        /// <summary>
        /// The delay in milliseconds between MTU resend attempts.
        /// </summary>
        public int MTUAttemptDelay = 1000;
        /// <summary>
        /// The MTU growth factor.
        /// </summary>
        public double MTUGrowthFactor = 1.25;

        /// <summary>
        /// The maximum amount of fragments allowed to be used.
        /// </summary>
        public int MaxFragments = 512;

        // Memory
        /// <summary>
        /// The maxmimum packet size. Should be larger than the MTU.
        /// </summary>
        public int MaxBufferSize = 1024 * 5;

        // Timeouts
        /// <summary>
        /// The amount of milliseconds from the connection request that the connection has to solve the challenge and complete the connection handshake.
        /// Note that this timeout only starts counting after the connection request has been approved.
        /// </summary>
        public int HandshakeTimeout = 20_000;
        /// <summary>
        /// The amount of milliseconds of packet silence before a already connected connection will be disconnected.
        /// </summary>
        public int ConnectionTimeout = 20_000;
        /// <summary>
        /// The amount milliseconds between heartbeat keep-alive packets are sent.
        /// </summary>
        public int HeartbeatDelay = 5000;

        // Handshake resends
        /// <summary>
        /// The amount of milliseconds between resends during the handshake process.
        /// </summary>
        public int HandshakeResendDelay = 500;
        /// <summary>
        /// The maximum amount of packet resends to perform per stage of the handshake process.
        /// </summary>
        public int MaxHandshakeResends = 20;

        // Connection request resends
        /// <summary>
        /// The delay between connection request resends in milliseconds.
        /// </summary>
        public int ConnectionRequestMinResendDelay = 500;
        /// <summary>
        /// The maximum amount of connection requests to be sent.
        /// </summary>
        public int MaxConnectionRequestResends = 5;
        /// <summary>
        /// The amount of time in milliseconds before a pending connection times out.
        /// </summary>
        public int ConnectionRequestTimeout = 5000;

        // Security
        /// <summary>
        /// The difficulty of the challenge in bits. Higher difficulties exponentially increase the solve time.
        /// </summary>
        public int ChallengeDifficulty = 20;
        /// <summary>
        /// The amount of successfull initialization vectors to keep for initial connection requests.
        /// </summary>
        public int ConnectionChallengeHistory = 2048;
        /// <summary>
        /// The connection request challenge time window in seconds.
        /// </summary>
        public int ConnectionChallengeTimeWindow = 60 * 5;
        /// <summary>
        /// Whether or not to enable time based connection challenge. 
        /// Enabling this will prevent slot filling attacks but requires the connector and connection receivers times to be synced with a diff of
        /// no more than ((RTT / 2) + ConnectionChallengeTimeWindow) in either direction.
        /// This is a perfectly reasonable expectation. The time is sent as UTC.
        /// </summary>
        public bool TimeBasedConnectionChallenge = true;

        // Denial Of Service
        /// <summary>
        /// The amplification prevention padding of handshake requests. 
        /// All handshake packets sent by the connector will be of this size.
        /// </summary>
        public int AmplificationPreventionHandshakePadding = 512;

        // Channels
        /// <summary>
        /// The channel types, the indexes of which becomes the channelId.
        /// </summary>
        public ChannelType[] ChannelTypes = new ChannelType[0];

        // Channel performance
        /// <summary>
        /// The window size for reliable packets, reliable acks and unrelaible acks.
        /// </summary>
        public int ReliabilityWindowSize = 512;
        /// <summary>
        /// The window size for last ack times.
        /// </summary>
        public int ReliableAckFlowWindowSize = 1024;
        /// <summary>
        /// The maximum amount of resends reliable channels will attempt per packet before timing the connection out.
        /// </summary>
        public int ReliabilityMaxResendAttempts = 30;
        /// <summary>
        /// The resend time multiplier. The resend delay for reliable packets is (RTT * ReliabilityResendRoundtripMultiplier).
        /// This is to account for flucuations in the network.
        /// </summary>
        public double ReliabilityResendRoundtripMultiplier = 1.2;
        /// <summary>
        /// The minimum delay before a reliale packet is resent.
        /// </summary>
        public int ReliabilityMinPacketResendDelay = 100;
        /// <summary>
        /// The minimum delay before an ack is resent.
        /// </summary>
        public int ReliabilityMinAckResendDelay = 100;

        // Bandwidth limitation
        /// <summary>
        /// Constructor delegate for creating the desired bandwidth tracker.
        /// </summary>
        public Func<IBandwidthTracker> CreateBandwidthTracker = () => new SimpleBandwidthTracker(1024 * 1024 * 8, 2, 0.5f); // 8 kilobytes per second allowed, resets every 2 seconds with a 50% remainder carry

        // Simulation
        /// <summary>
        /// Whether or not to enable the network condition simulator.
        /// </summary>
        public bool UseSimulator = false;
        /// <summary>
        /// The configuration for the network simulator.
        /// </summary>
        public SimulatorConfig SimulatorConfig = new SimulatorConfig()
        {
            DropPercentage = 0.2f,
            MaxLatency = 2000,
            MinLatency = 50
        };

        // Advanced protocol settings (usually these should NOT be fucked with. Please understand their full meaning before changing)
        /// <summary>
        /// Whether or not heartbeats should be sent and processed. 
        /// Disabling this requires you to ensure the connection stays alive by sending constant packets yourself.
        /// </summary>
        public bool EnableHeartbeats = true;
        /// <summary>
        /// Whether or not timeouts should be enabled. 
        /// Disabling this means connection requests and connected connections will never time out. Not recommended.
        /// </summary>
        public bool EnableTimeouts = true;
        /// <summary>
        /// Whether or not to enable channel updates.
        /// Disabling this will prevent channels such as Reliable channels to resend packets.
        /// </summary>
        public bool EnableChannelUpdates = true;
        /// <summary>
        /// Whether or not packets should be resent during the connection handshake.
        /// Disabling this requires 0 packet loss during the handshake.
        /// </summary>
        public bool EnableConnectionRequestResends = true;
        /// <summary>
        /// Whether or not packet merging should be enabled.
        /// </summary>
        public bool EnablePacketMerging = true;
        /// <summary>
        /// Whether or not acks should be reported to the user.
        /// </summary>
        public bool EnableAckNotifications = true;
        /// <summary>
        /// Whether or not to enable bandwidth tracking.
        /// </summary>
        public bool EnableBandwidthTracking = true;

        public List<string> GetInvalidConfiguration()
        {
            List<string> messages = new List<string>();

            if (MaxMergeMessageSize > MaximumMTU)
            {
                messages.Add("MaxMergeMessageSize cannot be greater than MaxMessageSize");
            }

            if (AmplificationPreventionHandshakePadding > MaximumMTU)
            {
                messages.Add("AmplificationPreventionHandshakePadding cannot be greater than MaxMessageSize");
            }

            if (ChannelTypes.Length > Constants.MAX_CHANNELS)
            {
                messages.Add("Cannot have more than " + Constants.MAX_CHANNELS + " channels");
            }

            if (SocketThreads < 1)
            {
                messages.Add("SocketThreads cannot be less than 1");
            }

            if (LogicThreads < 0)
            {
                messages.Add("LogicThreads cannot be less than 0. Use 0 to process on the SocketThread");
            }

            if (ProcessingThreads < 0)
            {
                messages.Add("ProcessingThreads cannot be less than 0. Use 0 to process on the SocketThread");
            }

            for (int i = 0; i < ChannelTypes.Length; i++)
            {
                if (!ChannelTypeUtils.IsValidChannelType(ChannelTypes[i]))
                {
                    messages.Add("ChannelType at index " + i + " is not a valid ChannelType");
                }
            }

            if (DualListenPort > ushort.MaxValue)
            {
                messages.Add("DualListenPort cannot be greater than " + ushort.MaxValue);
            }

            if (DualListenPort < 0)
            {
                messages.Add("DualListenPort cannot be less than 0. Use 0 to get a random port");
            }

            if (IPv4ListenAddress == null)
            {
                messages.Add("IPv4ListenAddress cannot be null");
            }

            if (IPv6ListenAddress == null)
            {
                messages.Add("IPv6ListenAddress cannot be null");
            }

            if (LogicDelay < 0)
            {
                messages.Add("LogicDelay cannot be less than 0. Use 0 for no delay");
            }

            if (MaxMergeMessageSize < 32)
            {
                messages.Add("MaxMergeMessageSize cannot be less than 32. Set EnablePacketMerging to false to disable merging");
            }

            if (MaxMergeDelay < 0)
            {
                messages.Add("MaxMergeDelay cannot be less than 0. Set EnablePacketMerging to false to disable merging");
            }

            if (MergedAckBytes < 1)
            {
                messages.Add("MergedAckBytes cannot be less than 1. Set EnableMergedAcks to false to disable merged acks");
            }

            if (MinimumMTU < Constants.MINIMUM_MTU)
            {
                messages.Add("MinimumMTU cannot be less than " + Constants.MINIMUM_MTU);
            }

            if (MaxBufferSize < MaximumMTU)
            {
                messages.Add("MaxBufferSize cannot be less than MaximumMTU");
            }

            if (MaximumMTU < MinimumMTU)
            {
                messages.Add("MaximumMTU cannot be less than MinimumMTU");
            }

            if (MaxMTUAttempts < 1)
            {
                messages.Add("MaxMTUAttempts cannot be less than 1. Set EnablePathMTU to false to disable PathMTU");
            }

            if (MTUAttemptDelay < 0)
            {
                messages.Add("MTUAttemptDelay cannot be less than 0");
            }

            if (MTUGrowthFactor < 1)
            {
                messages.Add("MTUGrowthFactor cannot be less than 1");
            }

            if (MaxFragments < 1)
            {
                messages.Add("MaxFragments cannot be less than 1");
            }

            if (MaxFragments > Constants.MAX_FRAGMENTS)
            {
                messages.Add("MaxFragments cannot be greater than " + Constants.MAX_FRAGMENTS);
            }

            if (HandshakeTimeout < 0)
            {
                messages.Add("HandshakeTimeout cannot be less than 0");
            }

            if (ConnectionTimeout < 0)
            {
                messages.Add("ConnectionTimeout cannot be less than 0");
            }

            if (HeartbeatDelay < 0)
            {
                messages.Add("HeartbeatDelay cannot be less than 0");
            }

            if (HandshakeResendDelay < 0)
            {
                messages.Add("HandshakeResendDelay cannot be less than 0");
            }

            if (MaxHandshakeResends < 0)
            {
                messages.Add("MaxHandshakeResends cannot be less than 0");
            }

            if (ConnectionRequestMinResendDelay < 0)
            {
                messages.Add("ConnectionRequestMinResendDelay cannot be less than 0");
            }

            if (MaxConnectionRequestResends < 0)
            {
                messages.Add("MaxConnectionRequestResends cannot be less than 0");
            }

            if (ConnectionRequestTimeout < 0)
            {
                messages.Add("ConnectionRequestTimeout cannot be less than 0");
            }

            if (ChallengeDifficulty < 0)
            {
                messages.Add("ChallengeDifficulty cannot be less than 0");
            }

            if (ChallengeDifficulty > byte.MaxValue)
            {
                messages.Add("ChallengeDifficulty cannot be greater than " + byte.MaxValue);
            }

            if (ConnectionChallengeHistory < 0)
            {
                messages.Add("ConnectionChallengeHistory cannot  be less than 0");
            }

            if (ConnectionChallengeTimeWindow < 1)
            {
                messages.Add("ConnectionChallengeTimeWindow cannot be less than 1");
            }

            if (AmplificationPreventionHandshakePadding < 0)
            {
                messages.Add("AmplificationPreventionHandshakePadding cannot be less than 0");
            }

            if (ReliabilityWindowSize < 1)
            {
                messages.Add("ReliabilityWindowSize cannot be less than 1");
            }

            if (ReliableAckFlowWindowSize < 1)
            {
                messages.Add("ReliableAckFlowWindowSize cannot be less than 1");
            }

            if (ReliabilityMaxResendAttempts < 0)
            {
                messages.Add("ReliabilityMaxResendAttempts cannot be less than 0");
            }

            if (ReliabilityResendRoundtripMultiplier < 0)
            {
                messages.Add("ReliabilityResendRoundtripMultiplier cannot be less than 0");
            }

            if (ReliabilityMinPacketResendDelay < 0)
            {
                messages.Add("ReliabilityMinPacketResendDelay cannot be less than 0");
            }

            if (ReliabilityMinAckResendDelay < 0)
            {
                messages.Add("ReliabilityMinAckResendDelay cannot be less than 0");
            }

            if (SimulatorConfig.DropPercentage < 0)
            {
                messages.Add("SimulatorConfig.DropPercentage cannot be less than 0");
            }

            if (SimulatorConfig.DropPercentage > 1)
            {
                messages.Add("SimulatorConfig.DropPercentage cannot be greater than 1");
            }

            if (SimulatorConfig.MinLatency < 0)
            {
                messages.Add("SimulatorConfig.MinLatency cannot be less than 0");
            }

            if (SimulatorConfig.MaxLatency < 0)
            {
                messages.Add("SimulatorConfig.MaxLatency cannot be less than 0");
            }

            if (SimulatorConfig.MaxLatency < SimulatorConfig.MinLatency)
            {
                messages.Add("SimulatorConfig.MaxLatency cannot be less than SimulatorConfig.MinLatency");
            }

            if (EventQueueSize < 1)
            {
                messages.Add("EventQueueSize cannot be less than 1");
            }

            if (ProcessingQueueSize < 1)
            {
                messages.Add("ProcessingQueueSize cannot be less than 1");
            }

            if (HeapPointersPoolSize < 0)
            {
                messages.Add("HeapPointersPoolSize cannot be less than 0");
            }

            if (HeapMemoryPoolSize < 0)
            {
                messages.Add("HeapMemoryPoolSize cannot be less than 0");
            }

            if (MemoryWrapperPoolSize < 0)
            {
                messages.Add("MemoryWrapperPoolSize cannot be less than 0");
            }

            if (ChannelPoolSize < 0)
            {
                messages.Add("ChannelPoolSize cannot be less than 0");
            }

            if (EnableBandwidthTracking && CreateBandwidthTracker == null)
            {
                messages.Add("EnableBandwidthTracking is enabled but no CreateBandwidthTracker delegate is provided");
            }

            return messages;
        }
    }
}
