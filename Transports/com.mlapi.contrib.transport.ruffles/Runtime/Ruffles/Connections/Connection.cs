#define ALLOW_CONNECTION_STUB

using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using Ruffles.BandwidthTracking;
using Ruffles.Channeling;
using Ruffles.Channeling.Channels;
using Ruffles.Collections;
using Ruffles.Configuration;
using Ruffles.Core;
using Ruffles.Hashing;
using Ruffles.Memory;
using Ruffles.Messaging;
using Ruffles.Random;
using Ruffles.Time;
using Ruffles.Utils;

namespace Ruffles.Connections
{
    /// <summary>
    /// A connection between two RuffleSockets.
    /// </summary>
    public class Connection
    {
        /// <summary>
        /// Gets the connection identifier. This is safe to be used in user dictionaries.
        /// </summary>
        /// <value>The connection identifier.</value>
        public ulong Id { get; }
        /// <summary>
        /// Gets the connection state.
        /// </summary>
        /// <value>The connection state.</value>
        public ConnectionState State { get; internal set; }

        internal Connection NextConnection;
        internal Connection PreviousConnection;

        // States
        private MessageStatus HailStatus;
        private MessageStatus MTUStatus;

        /// <summary>
        /// Gets the current connection end point.
        /// </summary>
        /// <value>The connection end point.</value>
        public IPEndPoint EndPoint { get; }
        /// <summary>
        /// Gets the RuffleSocket the connection belongs to.
        /// </summary>
        /// <value>The RuffleSocket the connection belongs to.</value>
        public RuffleSocket Socket { get; }

        private ulong ConnectionChallenge { get; set; }
        private byte ChallengeDifficulty { get; set; }
        private ulong ChallengeAnswer { get; set; }

        /// <summary>
        /// Gets the time of the last outbound message.
        /// </summary>
        /// <value>The time of the last outbound message.</value>
        public NetTime LastMessageOut { get; private set; }
        /// <summary>
        /// Gets the time of the last incoming message.
        /// </summary>
        /// <value>The time of the last incoming message.</value>
        public NetTime LastMessageIn { get; private set; }
        /// <summary>
        /// Gets the time the connection was started.
        /// </summary>
        /// <value>The time the connection started.</value>
        public NetTime ConnectionStarted { get; private set; }
        /// <summary>
        /// Gets the time the handshake process started.
        /// </summary>
        /// <value>The time the handshake started.</value>
        public NetTime HandshakeStarted { get; private set; }
        /// <summary>
        /// Gets the time the connection was completed.
        /// </summary>
        /// <value>The time the connection was completed.</value>
        public NetTime ConnectionCompleted { get; private set; }
        /// <summary>
        /// Gets the estimated smoothed roundtrip.
        /// </summary>
        /// <value>The estimated smoothed roundtrip.</value>
        public int SmoothRoundtrip { get; private set; }
        /// <summary>
        /// Gets the mean roundtrip.
        /// </summary>
        /// <value>The roundtrip.</value>
        public int Roundtrip { get; private set; }
        /// <summary>
        /// Gets the roundtrip varience.
        /// </summary>
        /// <value>The roundtrip varience.</value>
        public int RoundtripVarience { get; private set; }
        /// <summary>
        /// Gets the lowest roundtrip time recorded.
        /// </summary>
        /// <value>The lowest roundtrip.</value>
        public int LowestRoundtrip { get; private set; }
        /// <summary>
        /// Gets the highest roundtrip varience recorded.
        /// </summary>
        /// <value>The highest roundtrip varience.</value>
        public int HighestRoundtripVarience { get; private set; }

        /// <summary>
        /// Gets the current bandwidth tracker.
        /// </summary>
        /// <value>The current bandwidth tracker.</value>
        public IBandwidthTracker BandwidthTracker { get; private set; }

        /// <summary>
        /// Gets the maximum amount of bytes that can be sent in a single message.
        /// </summary>
        /// <value>The maximum transmission unit.</value>
        public int MTU
        {
            get
            {
                return _mtu;
            }
            internal set
            {
                if (_mtu != value)
                {
                    _mtu = value;

                    if (OnMTUChanged != null)
                    {
                        OnMTUChanged(_mtu);
                    }
                }
            }
        }
        // Backing field for MTU property
        private int _mtu;
        /// <summary>
        /// Called when the MTU changes.
        /// </summary>
        public event MTUChangedDelegate OnMTUChanged;
        /// <summary>
        /// Delegate representing a MTU change.
        /// </summary>
        public delegate void MTUChangedDelegate(int MTU);

        private readonly UnreliableOrderedChannel HeartbeatChannel;
        private readonly MessageMerger Merger;
        internal readonly IChannel[] Channels = new IChannel[Constants.MAX_CHANNELS];

        // Pre connection challenge values
        internal ulong PreConnectionChallengeTimestamp;
        internal ulong PreConnectionChallengeCounter;
        internal bool PreConnectionChallengeSolved;
        internal ulong PreConnectionChallengeIV;


        // Handshake resend values
        internal int HandshakeResendAttempts;
        internal NetTime HandshakeLastSendTime;

        private MemoryManager MemoryManager => Socket.MemoryManager;
        private SocketConfig Config => Socket.Config;

        private readonly ReaderWriterLockSlim _stateLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

        internal Connection(ulong id, ConnectionState state, IPEndPoint endpoint, RuffleSocket socket)
        {
#if ALLOW_CONNECTION_STUB
            if (IsStub)
            {
                // NOOP
                return;
            }
#endif               
            this.Id = id;
            this.Socket = socket;
            this.EndPoint = endpoint;
            this.MTU = Config.MinimumMTU;
            this.SmoothRoundtrip = 0;
            this.HighestRoundtripVarience = 0;
            this.Roundtrip = 500;
            this.LowestRoundtrip = 500;
            this.LastMessageIn = NetTime.Now;
            this.LastMessageOut = NetTime.Now;
            this.ConnectionStarted = NetTime.Now;
            this.ConnectionCompleted = NetTime.Now;
            this.HandshakeStarted = NetTime.Now;
            this.HandshakeLastSendTime = NetTime.Now;
            this.HandshakeResendAttempts = 0;
            this.ChallengeAnswer = 0;
            this.ConnectionChallenge = RandomProvider.GetRandomULong();
            this.ChallengeDifficulty = (byte)Config.ChallengeDifficulty;
            this.PreConnectionChallengeTimestamp = 0;
            this.PreConnectionChallengeCounter = 0;
            this.PreConnectionChallengeIV = 0;
            this.PreConnectionChallengeSolved = false;
            this.State = state;

            if (Config.EnableBandwidthTracking && Config.CreateBandwidthTracker != null)
            {
                this.BandwidthTracker = Config.CreateBandwidthTracker();
            }

            if (Config.EnableHeartbeats)
            {
                this.HeartbeatChannel = new UnreliableOrderedChannel(0, this, Config, MemoryManager);
            }

            if (Config.EnablePacketMerging)
            {
                this.Merger = new MessageMerger(Config.MaxMergeMessageSize, Config.MinimumMTU, Config.MaxMergeDelay);
            }
        }

        /// <summary>
        /// Disconnect the specified connection.
        /// </summary>
        /// <param name="sendMessage">If set to <c>true</c> the remote will be notified of the disconnect rather than timing out.</param>
        public void Disconnect(bool sendMessage)
        {
            DisconnectInternal(sendMessage, false);
        }

        /// <summary>
        /// Sends the specified payload to a connection.
        /// This will send the packet straight away. 
        /// This can cause the channel to lock up. 
        /// For higher performance sends, use SendLater.
        /// </summary>
        /// <param name="payload">The payload to send.</param>
        /// <param name="channelId">The channel index to send the payload over.</param>
        /// <param name="noMerge">If set to <c>true</c> the message will not be merged.</param>
        public bool Send(ArraySegment<byte> payload, byte channelId, bool noMerge, ulong notificationKey)
        {
            return HandleChannelSend(payload, channelId, noMerge, notificationKey);
        }

        internal void SendInternal(ArraySegment<byte> payload, bool noMerge)
        {
#if ALLOW_CONNECTION_STUB
            if (IsStub)
            {
                // NOOP
                return;
            }
#endif

            // Check if there is enough bandwidth to spare for the packet
            if (BandwidthTracker == null || BandwidthTracker.TrySend(payload.Count))
            {
                LastMessageOut = NetTime.Now;

                bool merged = false;

                if (!Socket.Config.EnablePacketMerging || noMerge || !(merged = Merger.TryWrite(payload)))
                {
                    if (Socket.Simulator != null)
                    {
                        Socket.Simulator.Add(this, payload);
                    }
                    else
                    {
                        Socket.SendRaw(EndPoint, payload);
                    }
                }
            }
            else
            {
                // Packet was dropped
                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Message to connection " + this.Id + " was dropped due to bandwidth constraits");
            }
        }

        internal bool HandleChannelSend(ArraySegment<byte> data, byte channelId, bool noMerge, ulong notificationKey)
        {
            _stateLock.EnterReadLock();

            try
            {
                if (State == ConnectionState.Connected)
                {
                    // Send the data
                    ChannelRouter.CreateOutgoingMessage(data, this, channelId, noMerge, notificationKey);

                    return true;
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }

            return false;
        }

        internal void DisconnectInternal(bool sendMessage, bool timeout)
        {
#if ALLOW_CONNECTION_STUB
            if (IsStub)
            {
                // NOOP
                return;
            }
#endif

            // TODO: Could be just a normal write lock. The chance of it not being a write in the end is small.
            _stateLock.EnterUpgradeableReadLock();

            try
            {
                if (State == ConnectionState.Connected && sendMessage && !timeout)
                {
                    // Send disconnect message

                    // Allocate memory
                    HeapMemory memory = MemoryManager.AllocHeapMemory(1);

                    // Write disconnect header
                    memory.Buffer[0] = HeaderPacker.Pack(MessageType.Disconnect);

                    // Send disconnect message
                    SendInternal(new ArraySegment<byte>(memory.Buffer, 0, (int)memory.VirtualCount), true);

                    // Release memory
                    MemoryManager.DeAlloc(memory);
                }

                if (State != ConnectionState.Disconnected)
                {
                    _stateLock.EnterWriteLock();

                    try
                    {
                        // Set the state to disconnected
                        State = ConnectionState.Disconnected;

                        // Release all memory
                        Release();
                    }
                    finally
                    {
                        _stateLock.ExitWriteLock();
                    }

                    // Remove from global lookup
                    Socket.RemoveConnection(this);

                    // Send disconnect to userspace
                    Socket.PublishEvent(new NetworkEvent()
                    {
                        Connection = this,
                        Socket = Socket,
                        Type = timeout ? NetworkEventType.Timeout : NetworkEventType.Disconnect,
                        AllowUserRecycle = false,
                        ChannelId = 0,
                        Data = new ArraySegment<byte>(),
                        InternalMemory = null,
                        SocketReceiveTime = NetTime.Now,
                        MemoryManager = MemoryManager,
                        EndPoint = EndPoint
                    });
                }
            }
            finally
            {
                _stateLock.ExitUpgradeableReadLock();
            }
        }

        internal void AddRoundtripSample(ulong sample)
        {
            if (sample == 0)
            {
                sample = 1;
            }

            if (SmoothRoundtrip == 0)
            {
                SmoothRoundtrip = (int)((1 - 0.125) + 0.125 * sample);
            }
            else
            {
                SmoothRoundtrip = (int)((1 - 0.125) * SmoothRoundtrip + 0.125 * sample);
            }

            RoundtripVarience -= (RoundtripVarience / 4);

            if (SmoothRoundtrip >= Roundtrip)
            {
                Roundtrip += (SmoothRoundtrip - Roundtrip) / 8;
                RoundtripVarience += (SmoothRoundtrip - Roundtrip) / 4;
            }
            else
            {
                Roundtrip -= (Roundtrip - SmoothRoundtrip) / 8;
                RoundtripVarience += (Roundtrip - SmoothRoundtrip) / 4;
            }

            if (Roundtrip < LowestRoundtrip)
            {
                LowestRoundtrip = Roundtrip;
            }

            if (RoundtripVarience > HighestRoundtripVarience)
            {
                HighestRoundtripVarience = RoundtripVarience;
            }
        }

        internal void HandleHail(ArraySegment<byte> channelTypes)
        {
            _stateLock.EnterUpgradeableReadLock();

            try
            {
                if (State == ConnectionState.Connected)
                {
                    // TODO: Add time check
                    // We already got this hail. Send new confirmation.

                    SendHailConfirmed();
                }
                else if (State == ConnectionState.SolvingChallenge)
                {
                    _stateLock.EnterWriteLock();

                    try
                    {
                        for (byte i = 0; i < channelTypes.Count; i++)
                        {
                            // Assign the channel
                            Channels[i] = Socket.ChannelPool.GetChannel(ChannelTypeUtils.FromByte(channelTypes.Array[channelTypes.Offset + i]), i, this, Config, MemoryManager);
                        }

                        // Change state to connected
                        State = ConnectionState.Connected;
                    }
                    finally
                    {
                        _stateLock.ExitWriteLock();
                    }

                    // Print connected
                    if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogInfo("Client " + EndPoint + " successfully connected");

                    SendHailConfirmed();

                    // Send to userspace
                    Socket.PublishEvent(new NetworkEvent()
                    {
                        Connection = this,
                        Socket = Socket,
                        Type = NetworkEventType.Connect,
                        AllowUserRecycle = false,
                        ChannelId = 0,
                        Data = new ArraySegment<byte>(),
                        InternalMemory = null,
                        SocketReceiveTime = NetTime.Now,
                        MemoryManager = MemoryManager,
                        EndPoint = EndPoint
                    });
                }
            }
            finally
            {
                _stateLock.ExitUpgradeableReadLock();
            }
        }

        internal void HandleHeartbeat(ArraySegment<byte> payload)
        {
            _stateLock.EnterReadLock();

            try
            {
                if (State == ConnectionState.Connected)
                {
                    HeapPointers pointers = HeartbeatChannel.HandleIncomingMessagePoll(payload);

                    if (pointers != null)
                    {
                        MemoryWrapper wrapper = (MemoryWrapper)pointers.Pointers[0];

                        if (wrapper != null)
                        {
                            if (wrapper.AllocatedMemory != null)
                            {
                                LastMessageIn = NetTime.Now;

                                // Dealloc the memory
                                MemoryManager.DeAlloc(wrapper.AllocatedMemory);
                            }

                            if (wrapper.DirectMemory != null)
                            {
                                LastMessageIn = NetTime.Now;
                            }

                            // Dealloc the wrapper
                            MemoryManager.DeAlloc(wrapper);
                        }

                        // Dealloc the pointers
                        MemoryManager.DeAlloc(pointers);
                    }
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        internal void HandleMTURequest(uint size)
        {
            // This does not access anything shared. We thus dont need to lock the state. If the state is raced or outdated its no harm done.
            // TODO: Remove lock

            _stateLock.EnterReadLock();

            try
            {
                if (State == ConnectionState.Connected)
                {
                    // Alloc memory for response
                    HeapMemory memory = MemoryManager.AllocHeapMemory(size);

                    // Write the header
                    memory.Buffer[0] = HeaderPacker.Pack(MessageType.MTUResponse);

                    // Send the response
                    SendInternal(new ArraySegment<byte>(memory.Buffer, 0, (int)memory.VirtualCount), true);

                    // Dealloc the memory
                    MemoryManager.DeAlloc(memory);
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        internal void HandleMTUResponse(uint size)
        {
            _stateLock.EnterReadLock();

            try
            {
                if (State == ConnectionState.Connected)
                {
                    // Calculate the new MTU
                    int attemptedMtu = (int)(MTU * Config.MTUGrowthFactor);

                    if (attemptedMtu > Config.MaximumMTU)
                    {
                        attemptedMtu = Config.MaximumMTU;
                    }

                    if (attemptedMtu < Config.MinimumMTU)
                    {
                        attemptedMtu = Config.MinimumMTU;
                    }

                    if (attemptedMtu == size)
                    {
                        // This is a valid response

                        if (Merger != null)
                        {
                            Merger.ExpandToSize((int)attemptedMtu);
                        }

                        // Set new MTU
                        MTU = (ushort)attemptedMtu;

                        // Set new status
                        MTUStatus = new MessageStatus()
                        {
                            Attempts = 0,
                            HasAcked = false,
                            LastAttempt = NetTime.MinValue
                        };

                        if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Client " + EndPoint + " MTU was increased to " + MTU);
                    }
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        internal void HandleChannelData(ArraySegment<byte> payload)
        {
            _stateLock.EnterReadLock();

            try
            {
                if (State == ConnectionState.Connected)
                {
                    LastMessageIn = NetTime.Now;

                    ChannelRouter.HandleIncomingMessage(payload, this, Config, MemoryManager);
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        internal void HandleChannelAck(ArraySegment<byte> payload)
        {
            _stateLock.EnterReadLock();

            try
            {
                if (State == ConnectionState.Connected)
                {
                    LastMessageIn = NetTime.Now;

                    ChannelRouter.HandleIncomingAck(payload, this, Config, MemoryManager);
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        internal void HandleHailConfirmed()
        {
            // If the state is changed during a confirmation it does not matter. No shared data is used except the HailStatus which is fine.
            // If reusing connection, this could become a race.
            // TODO: Remove lock

            _stateLock.EnterReadLock();

            try
            {
                if (State == ConnectionState.Connected && !HailStatus.HasAcked)
                {
                    LastMessageIn = NetTime.Now;
                    HailStatus.HasAcked = true;
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        internal void HandleChallengeRequest(ulong challenge, byte difficulty)
        {
            _stateLock.EnterReadLock();

            try
            {
                if (State == ConnectionState.RequestingConnection)
                {
                    LastMessageIn = NetTime.Now;

                    // Solve the hashcash
                    if (HashCash.TrySolve(challenge, difficulty, ulong.MaxValue, out ulong additionsRequired))
                    {
                        if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Solved the challenge");

                        // Set the solved results
                        ConnectionChallenge = challenge;
                        ChallengeDifficulty = difficulty;
                        ChallengeAnswer = additionsRequired;

                        // Set resend values
                        HandshakeResendAttempts = 0;
                        HandshakeStarted = NetTime.Now;
                        HandshakeLastSendTime = NetTime.Now;
                        State = ConnectionState.SolvingChallenge;

                        // Send the response
                        SendChallengeResponse();
                    }
                    else
                    {
                        // Failed to solve
                        if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Failed to solve the challenge");
                    }
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        internal void HandleChallengeResponse(ulong proposedSolution)
        {
            _stateLock.EnterUpgradeableReadLock();

            try
            {
                if (State == ConnectionState.RequestingChallenge)
                {
                    // Check if it is solved
                    bool isCollided = HashCash.Validate(ConnectionChallenge, proposedSolution, ChallengeDifficulty);

                    if (isCollided)
                    {
                        // Success, they completed the hashcash challenge

                        if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Client " + EndPoint + " successfully completed challenge of difficulty " + ChallengeDifficulty);

                        // Assign the channels
                        for (byte i = 0; i < Config.ChannelTypes.Length; i++)
                        {
                            Channels[i] = Socket.ChannelPool.GetChannel(Config.ChannelTypes[i], i, this, Config, MemoryManager);
                        }

                        // Reset hail status
                        HailStatus = new MessageStatus()
                        {
                            Attempts = 0,
                            HasAcked = false,
                            LastAttempt = NetTime.MinValue
                        };


                        _stateLock.EnterWriteLock();

                        try
                        {
                            // Change state to connected
                            State = ConnectionState.Connected;
                        }
                        finally
                        {
                            _stateLock.ExitWriteLock();
                        }

                        // Save time
                        ConnectionCompleted = NetTime.Now;

                        // Print connected
                        if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogInfo("Client " + EndPoint + " successfully connected");

                        // Send hail
                        SendHail();

                        // Send to userspace
                        Socket.PublishEvent(new NetworkEvent()
                        {
                            Connection = this,
                            Socket = Socket,
                            Type = NetworkEventType.Connect,
                            AllowUserRecycle = false,
                            ChannelId = 0,
                            Data = new ArraySegment<byte>(),
                            InternalMemory = null,
                            SocketReceiveTime = NetTime.Now,
                            MemoryManager = MemoryManager,
                            EndPoint = EndPoint
                        });
                    }
                    else
                    {
                        // Failed, disconnect them
                        if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Client " + EndPoint + " failed the challenge");
                    }
                }
            }
            finally
            {
                _stateLock.ExitUpgradeableReadLock();
            }
        }

        internal void SendHail()
        {
            HailStatus.Attempts++;
            HailStatus.LastAttempt = NetTime.Now;

            // Packet size
            int size = 2 + (byte)Config.ChannelTypes.Length;

            // Allocate memory
            HeapMemory memory = MemoryManager.AllocHeapMemory((uint)size);

            // Write the header
            memory.Buffer[0] = HeaderPacker.Pack(MessageType.Hail);

            // Write the amount of channels
            memory.Buffer[1] = (byte)Config.ChannelTypes.Length;

            // Write the channel types
            for (byte i = 0; i < (byte)Config.ChannelTypes.Length; i++)
            {
                memory.Buffer[2 + i] = ChannelTypeUtils.ToByte(Config.ChannelTypes[i]);
            }

            // Send the response
            SendInternal(new ArraySegment<byte>(memory.Buffer, 0, (int)memory.VirtualCount), true);

            // Release memory
            MemoryManager.DeAlloc(memory);

            // Print Debug
            if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Client " + EndPoint + " was sent a hail");
        }

        internal void SendHailConfirmed()
        {
            // Allocate memory
            HeapMemory memory = MemoryManager.AllocHeapMemory(1);

            // Write the header
            memory.Buffer[0] = HeaderPacker.Pack(MessageType.HailConfirmed);

            // Send confirmation
            SendInternal(new ArraySegment<byte>(memory.Buffer, 0, (int)memory.VirtualCount), true);

            // Release memory
            MemoryManager.DeAlloc(memory);

            if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Hail confirmation sent to " + EndPoint);
        }

        internal void SendChallengeResponse()
        {
            // Set resend values
            HandshakeResendAttempts++;
            HandshakeLastSendTime = NetTime.Now;

            // Calculate the minimum size we can fit the packet in
            int minSize = 1 + sizeof(ulong);

            // Calculate the actual size with respect to amplification padding
            int size = Math.Max(minSize, (int)Config.AmplificationPreventionHandshakePadding);

            // Allocate memory
            HeapMemory memory = MemoryManager.AllocHeapMemory((uint)size);

            // Write the header
            memory.Buffer[0] = HeaderPacker.Pack(MessageType.ChallengeResponse);

            // Write the challenge response
            for (byte i = 0; i < sizeof(ulong); i++) memory.Buffer[1 + i] = ((byte)(ChallengeAnswer >> (i * 8)));

            // Send the challenge response
            SendInternal(new ArraySegment<byte>(memory.Buffer, 0, (int)memory.VirtualCount), true);

            // Release memory
            MemoryManager.DeAlloc(memory);

            // Print debug
            if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Server " + EndPoint + " challenge of difficulty " + ChallengeDifficulty + " was solved. Answer was sent. [CollidedValue=" + ChallengeAnswer + "]");
        }

        internal void SendChallengeRequest()
        {
            // Set resend values
            HandshakeResendAttempts++;
            HandshakeLastSendTime = NetTime.Now;

            // Packet size
            uint size = 1 + sizeof(ulong) + 1;

            // Allocate memory
            HeapMemory memory = MemoryManager.AllocHeapMemory(size);

            // Write the header
            memory.Buffer[0] = HeaderPacker.Pack(MessageType.ChallengeRequest);

            // Write connection challenge
            for (byte i = 0; i < sizeof(ulong); i++) memory.Buffer[1 + i] = ((byte)(ConnectionChallenge >> (i * 8)));

            // Write the challenge difficulty
            memory.Buffer[1 + sizeof(ulong)] = (byte)ChallengeDifficulty;

            // Send the challenge
            SendInternal(new ArraySegment<byte>(memory.Buffer, 0, (int)memory.VirtualCount), true);

            // Release memory
            MemoryManager.DeAlloc(memory);

            if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Client " + EndPoint + " was sent a challenge of difficulty " + ChallengeDifficulty);
        }

        internal void Update()
        {
            if (Config.EnablePathMTU)
            {
                RunPathMTU();
            }

            if (Config.EnableChannelUpdates)
            {
                RunChannelUpdates();
            }

            if (Config.EnablePacketMerging)
            {
                CheckMergedPackets();
            }

            if (Config.EnableTimeouts)
            {
                CheckConnectionTimeouts();
            }

            if (Config.EnableHeartbeats)
            {
                CheckConnectionHeartbeats();
            }

            if (Config.EnableConnectionRequestResends)
            {
                CheckConnectionResends();
            }
        }

        private void RunPathMTU()
        {
            _stateLock.EnterReadLock();

            try
            {
                if (State == ConnectionState.Connected && MTU < Config.MaximumMTU && MTUStatus.Attempts < Config.MaxMTUAttempts && (NetTime.Now - MTUStatus.LastAttempt).TotalMilliseconds > Config.MTUAttemptDelay)
                {
                    int attemptedMtu = (int)(MTU * Config.MTUGrowthFactor);

                    if (attemptedMtu > Config.MaximumMTU)
                    {
                        attemptedMtu = Config.MaximumMTU;
                    }

                    if (attemptedMtu < Config.MinimumMTU)
                    {
                        attemptedMtu = Config.MinimumMTU;
                    }

                    MTUStatus.Attempts++;
                    MTUStatus.LastAttempt = NetTime.Now;

                    // Allocate memory
                    HeapMemory memory = MemoryManager.AllocHeapMemory((uint)attemptedMtu);

                    // Set the header
                    memory.Buffer[0] = HeaderPacker.Pack(MessageType.MTURequest);

                    // Send the request
                    SendInternal(new ArraySegment<byte>(memory.Buffer, 0, (int)memory.VirtualCount), true);

                    // Dealloc the memory
                    MemoryManager.DeAlloc(memory);
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        private void RunChannelUpdates()
        {
            bool timeout = false;

            _stateLock.EnterReadLock();

            try
            {
                if (State == ConnectionState.Connected)
                {
                    for (int i = 0; i < Channels.Length; i++)
                    {
                        if (Channels[i] != null)
                        {
                            Channels[i].InternalUpdate(out bool _timeout);

                            timeout |= _timeout;
                        }
                    }
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }

            if (timeout)
            {
                DisconnectInternal(false, true);
            }
        }

        private void CheckConnectionResends()
        {
            _stateLock.EnterReadLock();

            try
            {
                switch (State)
                {
                    case ConnectionState.RequestingConnection:
                        {
                            if ((!Config.TimeBasedConnectionChallenge || PreConnectionChallengeSolved) && (NetTime.Now - HandshakeLastSendTime).TotalMilliseconds > Config.ConnectionRequestMinResendDelay && HandshakeResendAttempts <= Config.MaxConnectionRequestResends)
                            {
                                HandshakeResendAttempts++;
                                HandshakeLastSendTime = NetTime.Now;

                                // Calculate the minimum size we can fit the packet in
                                int minSize = 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + (Config.TimeBasedConnectionChallenge ? sizeof(ulong) * 3 : 0);

                                // Calculate the actual size with respect to amplification padding
                                int size = Math.Max(minSize, (int)Config.AmplificationPreventionHandshakePadding);

                                // Allocate memory
                                HeapMemory memory = MemoryManager.AllocHeapMemory((uint)size);

                                // Write the header
                                memory.Buffer[0] = HeaderPacker.Pack((byte)MessageType.ConnectionRequest);

                                // Copy the identification token
                                Buffer.BlockCopy(Constants.RUFFLES_PROTOCOL_IDENTIFICATION, 0, memory.Buffer, 1, Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length);

                                if (Config.TimeBasedConnectionChallenge)
                                {
                                    // Write the response unix time
                                    for (byte x = 0; x < sizeof(ulong); x++) memory.Buffer[1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + x] = ((byte)(PreConnectionChallengeTimestamp >> (x * 8)));

                                    // Write counter
                                    for (byte x = 0; x < sizeof(ulong); x++) memory.Buffer[1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + sizeof(ulong) + x] = ((byte)(PreConnectionChallengeCounter >> (x * 8)));

                                    // Write IV
                                    for (byte x = 0; x < sizeof(ulong); x++) memory.Buffer[1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + (sizeof(ulong) * 2) + x] = ((byte)(PreConnectionChallengeIV >> (x * 8)));

                                    // Print debug
                                    if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Resending ConnectionRequest with challenge [Counter=" + PreConnectionChallengeCounter + "] [IV=" + PreConnectionChallengeIV + "] [Time=" + PreConnectionChallengeTimestamp + "] [Hash=" + HashProvider.GetStableHash64(PreConnectionChallengeTimestamp, PreConnectionChallengeCounter, PreConnectionChallengeIV) + "]");
                                }
                                else
                                {
                                    // Print debug
                                    if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Resending ConnectionRequest");
                                }

                                SendInternal(new ArraySegment<byte>(memory.Buffer, 0, (int)memory.VirtualCount), true);

                                // Release memory
                                MemoryManager.DeAlloc(memory);
                            }
                        }
                        break;
                    case ConnectionState.RequestingChallenge:
                        {
                            if ((NetTime.Now - HandshakeLastSendTime).TotalMilliseconds > Config.HandshakeResendDelay && HandshakeResendAttempts <= Config.MaxHandshakeResends)
                            {
                                // Resend challenge request
                                SendChallengeRequest();
                            }
                        }
                        break;
                    case ConnectionState.SolvingChallenge:
                        {
                            if ((NetTime.Now - HandshakeLastSendTime).TotalMilliseconds > Config.HandshakeResendDelay && HandshakeResendAttempts <= Config.MaxHandshakeResends)
                            {
                                // Resend response
                                SendChallengeResponse();
                            }
                        }
                        break;
                    case ConnectionState.Connected:
                        {
                            if (!HailStatus.HasAcked && (NetTime.Now - HailStatus.LastAttempt).TotalMilliseconds > Config.HandshakeResendDelay && HailStatus.Attempts <= Config.MaxHandshakeResends)
                            {
                                // Resend hail
                                SendHail();
                            }
                        }
                        break;
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        private void CheckMergedPackets()
        {
            _stateLock.EnterReadLock();

            try
            {
                if (State == ConnectionState.Connected)
                {
                    ArraySegment<byte>? mergedPayload = Merger.TryFlush(false);

                    if (mergedPayload != null)
                    {
                        SendInternal(mergedPayload.Value, true);
                    }
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        private void CheckConnectionTimeouts()
        {
            _stateLock.EnterReadLock();

            bool timeout = false;

            try
            {
                if (State == ConnectionState.RequestingConnection)
                {
                    if ((NetTime.Now - ConnectionStarted).TotalMilliseconds > Config.ConnectionRequestTimeout)
                    {
                        // This client has taken too long to connect. Let it go.
                        // TODO: This log can race and show multiple times
                        if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogInfo("Disconnecting client because handshake was not started");

                        timeout = true;
                    }
                }
                else if (State != ConnectionState.Connected)
                {
                    // They are not requesting connection. But they are not connected. This means they are doing a handshake
                    if ((NetTime.Now - HandshakeStarted).TotalMilliseconds > Config.HandshakeTimeout)
                    {
                        // This client has taken too long to connect. Let it go.
                        // TODO: This log can race and show multiple times
                       if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogInfo("Disconnecting client because it took too long to complete the handshake");

                        timeout = true;
                    }
                }
                else
                {
                    if ((NetTime.Now - LastMessageIn).TotalMilliseconds > Config.ConnectionTimeout)
                    {
                        // This client has not answered us in way too long. Let it go
                        // TODO: This log can race and show multiple times
                        if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogInfo("Disconnecting client because no incoming message has been received");

                        timeout = true;
                    }
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }

            if (timeout)
            {
                DisconnectInternal(false, true);
            }
        }

        private void CheckConnectionHeartbeats()
        {
            _stateLock.EnterReadLock();

            try
            {
                if (State == ConnectionState.Connected)
                {
                    if ((NetTime.Now - LastMessageOut).TotalMilliseconds > Config.HeartbeatDelay)
                    {
                        // This client has not been talked to in a long time. Send a heartbeat.

                        // Create sequenced heartbeat packet
                        HeapMemory heartbeatMemory = HeartbeatChannel.CreateOutgoingHeartbeatMessage();

                        // Send heartbeat
                        SendInternal(new ArraySegment<byte>(heartbeatMemory.Buffer, (int)heartbeatMemory.VirtualOffset, (int)heartbeatMemory.VirtualCount), false);

                        // DeAlloc the memory
                        MemoryManager.DeAlloc(heartbeatMemory);
                    }
                }
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }

        internal void Release()
        {
            if (Config.EnableHeartbeats)
            {
                // Release all memory from the heartbeat channel
                HeartbeatChannel.Release();
            }

            if (Config.EnablePacketMerging)
            {
                // Try to get one last flush
                ArraySegment<byte>? mergedPayload = Merger.TryFlush(true);

                if (mergedPayload != null)
                {
                    SendInternal(mergedPayload.Value, true);
                }

                // Clean the merger
                Merger.Clear();
            }

            // Reset all channels, releasing memory etc
            for (int i = 0; i < Channels.Length; i++)
            {
                if (Channels[i] != null)
                {
                    // Grab a ref to the channel
                    IChannel channel = Channels[i];

                    // Set the channel to null. Preventing further polls
                    Channels[i] = null;

                    if (Config.ReuseChannels)
                    {
                        // Return old channel to pool
                        Socket.ChannelPool.Return(channel);
                    }
                    else
                    {
                        // Simply release the memory
                        channel.Release();
                    }
                }
            }
        }

#if ALLOW_CONNECTION_STUB
        private bool IsStub { get; set; }

        // Used by Test project
        internal static Connection Stub(SocketConfig config)
        {
            return new Connection(0, ConnectionState.Connected, new IPEndPoint(IPAddress.Any, 0), new RuffleSocket(config))
            {
                IsStub = true,
                MTU = config.MinimumMTU
            };
        }
#endif
    }
}
