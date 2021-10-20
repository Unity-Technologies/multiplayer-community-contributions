using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Ruffles.Channeling;
using Ruffles.Collections;
using Ruffles.Configuration;
using Ruffles.Connections;
using Ruffles.Hashing;
using Ruffles.Memory;
using Ruffles.Messaging;
using Ruffles.Random;
using Ruffles.Simulation;
using Ruffles.Time;
using Ruffles.Utils;

namespace Ruffles.Core
{
    /// <summary>
    /// A dual IPv4 IPv6 socket using the Ruffles protocol.
    /// </summary>
    public sealed class RuffleSocket
    {
        // Separate connections and pending to prevent something like a slorris attack
        private readonly Dictionary<IPEndPoint, Connection> _addressConnectionLookup = new Dictionary<IPEndPoint, Connection>();
        private Connection _headConnection;
        private ulong _connectionIdCounter = 0;
        private readonly Queue<ulong> _releasedConnectionIds = new Queue<ulong>();

        // Lock for adding or removing connections. This is done to allow for a quick ref to be gained on the user thread when connecting.
        private readonly ReaderWriterLockSlim _connectionsLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

        private Socket _ipv4Socket;
        private Socket _ipv6Socket;

        private SlidingSet<ulong> _challengeInitializationVectors;
        private readonly object _challengeInitializationVectorsLock = new object();

        internal MemoryManager MemoryManager { get; private set; }
        internal NetworkSimulator Simulator { get; private set; }
        internal ChannelPool ChannelPool { get; private set; }
        internal readonly SocketConfig Config;

        private readonly List<Thread> _threads = new List<Thread>();

        /// <summary>
        /// Gets a value indicating whether this <see cref="T:Ruffles.Core.RuffleSocket"/> is running.
        /// </summary>
        /// <value><c>true</c> if is running; otherwise, <c>false</c>.</value>
        public bool IsRunning { get; private set; }
        /// <summary>
        /// Gets a value indicating whether this <see cref="T:Ruffles.Core.RuffleSocket"/> is terminated.
        /// </summary>
        /// <value><c>true</c> if is terminated; otherwise, <c>false</c>.</value>
        [Obsolete("Use IsIntiailized instead to determine whether this socket is terminated or not")]
        public bool IsTerminated => !IsInitialized;
        /// <summary>
        /// Gets a value indicating whether this <see cref="T:Ruffles.Core.RuffleSocket"/> is initialized.
        /// </summary>
        /// <value><c>true</c> if is initialized; otherwise, <c>false</c>.</value>
        public bool IsInitialized { get; private set; }
        // Lock for starting, stopping and shutting down
        private readonly object _stateLock = new object();
        /// <summary>
        /// Whether or not the current OS supports IPv6
        /// </summary>
        public static readonly bool SupportsIPv6 = Socket.OSSupportsIPv6;

        // Events

        // Syncronized event
        private readonly AutoResetEvent _syncronizedEvent = new AutoResetEvent(false);
        // Syncronized callbacks
        private readonly ReaderWriterLockSlim _syncronizedCallbacksLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private readonly List<NetTuple<SynchronizationContext, SendOrPostCallback>> _syncronizedCallbacks = new List<NetTuple<SynchronizationContext, SendOrPostCallback>>();
        // Event queue
        private ConcurrentCircularQueue<NetworkEvent> _userEventQueue;

        // Processing queue
        private ConcurrentCircularQueue<NetTuple<HeapMemory, IPEndPoint>> _processingQueue;

        /// <summary>
        /// Gets a syncronization event that is set when a event is received.
        /// </summary>
        /// <value>The syncronization event.</value>
        public AutoResetEvent SyncronizationEvent
        {
            get
            {
                if (!Config.EnableSyncronizationEvent)
                {
                    throw new InvalidOperationException("Cannot get syncronzation event when EnableSyncronizationEvent is false");
                }

                return _syncronizedEvent;
            }
        }

        /// <summary>
        /// Gets the local IPv4 listening endpoint.
        /// </summary>
        /// <value>The local IPv4 endpoint.</value>
        public IPEndPoint LocalIPv4EndPoint
        {
            get
            {
                if (_ipv4Socket == null)
                {
                    return new IPEndPoint(IPAddress.None, 0);
                }

                return (IPEndPoint)_ipv4Socket.LocalEndPoint;
            }
        }

        /// <summary>
        /// Gets the local IPv6 listening endpoint.
        /// </summary>
        /// <value>The local IPv6 endpoint.</value>
        public IPEndPoint LocalIPv6EndPoint
        {
            get
            {
                if (_ipv6Socket == null)
                {
                    return new IPEndPoint(IPAddress.IPv6None, 0);
                }

                return (IPEndPoint)_ipv6Socket.LocalEndPoint;
            }
        }

        public RuffleSocket(SocketConfig config)
        {
            this.Config = config;
        }

        /// <summary>
        /// Register a syncronized callback that is ran on a specific thread when a message arrives.
        /// </summary>
        public void RegisterSyncronizedCallback(SendOrPostCallback callback, SynchronizationContext syncContext = null)
        {
            if (!Config.EnableSyncronizedCallbacks)
            {
                throw new InvalidOperationException("Cannot register a syncronized callback when EnableSyncronizedCallbacks is false");
            }

            if (syncContext == null)
            {
                syncContext = SynchronizationContext.Current;
            }

            if (syncContext == null)
            {
                throw new ArgumentNullException(nameof(syncContext), "Cannot register callback without a valid SyncronizationContext");
            }

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback), "Cannot register a null callback");
            }

            _syncronizedCallbacksLock.EnterWriteLock();

            try
            {
                _syncronizedCallbacks.Add(new NetTuple<SynchronizationContext, SendOrPostCallback>(syncContext, callback));
            }
            finally
            {
                _syncronizedCallbacksLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Unregisters a syncronized callback.
        /// </summary>
        public void UnregisterSyncronizedCallback(SendOrPostCallback callback)
        {
            if (!Config.EnableSyncronizedCallbacks)
            {
                throw new InvalidOperationException("Cannot register a syncronized callback when EnableSyncronizedCallbacks is false");
            }

            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback), "Cannot unregister null callback");
            }

            _syncronizedCallbacksLock.EnterWriteLock();

            try
            {
                for (int i = _syncronizedCallbacks.Count - 1; i >= 0; i++)
                {
                    if (_syncronizedCallbacks[i].Item2 == callback)
                    {
                        _syncronizedCallbacks.RemoveAt(i);
                    }
                }
            }
            finally
            {
                _syncronizedCallbacksLock.ExitWriteLock();
            }
        }

        internal void PublishEvent(NetworkEvent @event)
        {
            _userEventQueue.Enqueue(@event);

            if (Config.EnableSyncronizationEvent)
            {
                _syncronizedEvent.Set();
            }

            if (Config.EnableSyncronizedCallbacks)
            {
                _syncronizedCallbacksLock.EnterReadLock();

                try
                {
                    for (int i = 0; i < _syncronizedCallbacks.Count; i++)
                    {
                        _syncronizedCallbacks[i].Item1.Post(_syncronizedCallbacks[i].Item2, this);
                    }
                }
                finally
                {
                    _syncronizedCallbacksLock.ExitReadLock();
                }
            }
        }

        private void Initialize()
        {
            if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogInfo("Initializing socket");

            if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Checking SocketConfig validity");

            List<string> configurationErrors = Config.GetInvalidConfiguration();

            if (configurationErrors.Count > 0)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Invalid configuration! Please fix the following issues [" + string.Join(",", configurationErrors.ToArray()) + "]");
            }
            else
            {
                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("SocketConfig is valid");
            }


            if (Config.UseSimulator)
            {
                Simulator = new NetworkSimulator(Config.SimulatorConfig, SendRaw);
                if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogInfo("Simulator ENABLED");
            }
            else
            {
                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Simulator DISABLED");
            }

            if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Allocating " + Config.EventQueueSize + " event slots");
            _userEventQueue = new ConcurrentCircularQueue<NetworkEvent>(Config.EventQueueSize);

            if (Config.ProcessingThreads > 0)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Allocating " + Config.ProcessingQueueSize + " processing slots");
                _processingQueue = new ConcurrentCircularQueue<NetTuple<HeapMemory, IPEndPoint>>(Config.ProcessingQueueSize);
            }
            else
            {
                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Not allocating processingQueue beucase ProcessingThreads is set to 0");
            }

            if (Config.TimeBasedConnectionChallenge)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Allocating " + Config.ConnectionChallengeHistory + " challenge IV slots");
                _challengeInitializationVectors = new SlidingSet<ulong>((int)Config.ConnectionChallengeHistory);
            }
            else
            {
                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Not allocating challenge IV cache beucase TimeBasedConnectionChallenge is set to false");
            }

            if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Allocating memory manager");
            MemoryManager = new MemoryManager(Config);

            if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Allocating channel pool");
            ChannelPool = new ChannelPool(Config);

            if (!NetTime.HighResolution)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("NetTime does not support high resolution. This might impact Ruffles performance");
            }

            // Reset the connectionId counter
            _connectionIdCounter = 0;
            // Reset the releasedConnectionIds collection
            _releasedConnectionIds.Clear();

            if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogInfo("Socket initialized");
        }

        /// <summary>
        /// Starts the socket.
        /// </summary>
        public bool Start()
        {
            lock (_stateLock)
            {
                if (IsRunning)
                {
                    throw new InvalidOperationException("Socket already started");
                }

                if (!IsInitialized)
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Binding socket");
                    bool bindSuccess = Bind(Config.IPv4ListenAddress, Config.IPv6ListenAddress, Config.DualListenPort, Config.UseIPv6Dual);

                    if (!bindSuccess)
                    {
                        if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Failed to bind socket");
                        return false;
                    }
                    else
                    {
                        if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogInfo("Socket was successfully bound");
                        Initialize();
                        IsInitialized = true;
                    }
                }

                // Create logic threads
                for (int i = 0; i < Config.LogicThreads; i++)
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Creating NetworkThread #" + i);

                    _threads.Add(new Thread(StartNetworkLogic)
                    {
                        Name = "NetworkThread #" + i,
                        IsBackground = true
                    });
                }

                // Create socket threads
                for (int i = 0; i < Config.SocketThreads; i++)
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Creating SocketThread #" + i);

                    _threads.Add(new Thread(StartSocketLogic)
                    {
                        Name = "SocketThread #" + i,
                        IsBackground = true
                    });
                }

                for (int i = 0; i < Config.ProcessingThreads; i++)
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Creating ProcessingThread #" + i);

                    _threads.Add(new Thread(StartPacketProcessing)
                    {
                        Name = "ProcessingThread #" + i,
                        IsBackground = true
                    });
                }

                // Set running state to true
                IsRunning = true;

                // Start threads
                for (int i = 0; i < _threads.Count; i++)
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Starting " + _threads[i].Name);

                    _threads[i].Start();
                }

                if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogInfo("Started " + (Config.LogicThreads + Config.SocketThreads + Config.ProcessingThreads) + " threads");

                return true;
            }
        }

        /// <summary>
        /// Stops the socket. This will flush all the events to the UserQueue allowing you to Poll them.
        /// </summary>
        public void Stop()
        {
            lock (_stateLock)
            {
                if (!IsRunning)
                {
                    throw new InvalidOperationException("Cannot stop a non running socket");
                }

                // Stop all the threads
                IsRunning = false;

                int threadCount = _threads.Count;

                // Wait for all the threads to exit
                for (int i = _threads.Count - 1; i >= 0; i--)
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Joining " + _threads[i].Name);

                    _threads[i].Join();
                    _threads.RemoveAt(i);
                }

                if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogInfo("Joined " + threadCount + " threads");

                // Empty the processing queue. This will consume all the unconsumed packets and they will be handled
                // This has to be done before we call DisconnectInternal in order for all packets to be processed.
                if (_processingQueue != null)
                {
                    EmptyPacketProcessingQueue();
                }

                // Disconnect all clients. This will send all the finals
                for (Connection connection = _headConnection; connection != null; connection = connection.NextConnection)
                {
                    connection.DisconnectInternal(true, false);
                }

                // Flush the simulator. Do this last to prevent any last packets sent by the connections to be dropped.
                if (Simulator != null)
                {
                    Simulator.Flush();
                }
            }
        }

        /// <summary>
        /// Shuts the socket down. This will clear the UserQueue which means Poll will not return any elements.
        /// If you want the final packets. Call Stop, then Poll, then Shutdown.
        /// </summary>
        public void Shutdown()
        {
            lock (_stateLock)
            {
                if (!IsInitialized)
                {
                    throw new InvalidOperationException("Cannot shutdown a non initialized socket");
                }

                IsInitialized = false;

                if (IsRunning)
                {
                    Stop();
                }

                // Release simulator
                Simulator = null;

                while (_userEventQueue != null && _userEventQueue.TryDequeue(out NetworkEvent networkEvent))
                {
                    // Recycle all packets to prevent leak detection
                    networkEvent.Recycle();
                }

                // Release user queue
                _userEventQueue = null;

                while (_processingQueue != null && _processingQueue.TryDequeue(out NetTuple<HeapMemory, IPEndPoint> packet))
                {
                    // Dealloc all the pending memory to prevent leak detection
                    MemoryManager.DeAlloc(packet.Item1);
                }

                // Release processing queue
                _processingQueue = null;

                // Release IV cache
                _challengeInitializationVectors = null;

                // Release channel pool
                ChannelPool = null;

                if (_ipv4Socket != null)
                {
                    // Close socket
                    _ipv4Socket.Close();
                    _ipv4Socket = null;
                }

                if (_ipv6Socket != null)
                {
                    // Close socket
                    _ipv6Socket.Close();
                    _ipv6Socket = null;
                }

                // Release ALL memory to GC safely. If this is not done the MemoryManager will see it as a leak
                MemoryManager.Release();
            }
        }

        private bool Bind(IPAddress addressIPv4, IPAddress addressIPv6, int port, bool ipv6Dual)
        {
            // Create IPv4 UDP Socket
            _ipv4Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            // Setup IPv4 Socket and properly bind it to the OS
            if (!SetupAndBind(_ipv4Socket, new IPEndPoint(addressIPv4, port)))
            {
                // Failed to bind socket
                return false;
            }

            int ipv4LocalPort = ((IPEndPoint)_ipv4Socket.LocalEndPoint).Port;

            if (!ipv6Dual || !SupportsIPv6)
            {
                // Dont use IPv6 dual mode
                return true;
            }

            // Create IPv6 UDP Socket
            _ipv6Socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);

            // Setup IPv6 socket and bind it to the same port as the IPv4 socket was bound to.
            // Ignore if it fails
            SetupAndBind(_ipv6Socket, new IPEndPoint(addressIPv6, ipv4LocalPort));

            return true;
        }

        private bool SetupAndBind(Socket socket, IPEndPoint endpoint)
        {
            // Dont fragment and broadcasting is only supported on IPv4
            if (socket.AddressFamily == AddressFamily.InterNetwork)
            {
                try
                {
                    socket.DontFragment = true;
                }
                catch (SocketException e)
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Failed to enable DontFragment: " + e);
                    // This shouldnt happen when the OS supports it.
                    // This is used for path MTU to do application level fragmentation
                }

                try
                {
                    socket.EnableBroadcast = true;
                }
                catch (SocketException e)
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Failed to enable broadcasting: " + e);
                }
            }

            try
            {
                // Set the .NET buffer sizes. Defaults to 1 megabyte each
                socket.ReceiveBufferSize = Constants.RECEIVE_SOCKET_BUFFER_SIZE;
                socket.SendBufferSize = Constants.SEND_SOCKET_BUFFER_SIZE;
            }
            catch (Exception e)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Failed to set socket buffer size: " + e);
            }

            try
            {
                const uint IOC_IN = 0x80000000;
                const uint IOC_VENDOR = 0x18000000;
                const uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;

                unchecked
                {
                    socket.IOControl((int)SIO_UDP_CONNRESET, new byte[] { 0 }, null);
                }
            }
            catch (Exception e)
            {
                // Ignore error when SIO_UDP_CONNRESET is not supported
                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Failed to set SIO_UDP_CONNRESET: " + e);
            }

            try
            {
                socket.Ttl = (short)Constants.SOCKET_PACKET_TTL;
            }
            catch (Exception e)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Failed to set TTL: " + e);
            }

            try
            {
                // Bind the socket to the OS
                socket.Bind(endpoint);
            }
            catch (SocketException bindException)
            {
                switch (bindException.SocketErrorCode)
                {
                    // IPv6 bind fix
                    case SocketError.AddressAlreadyInUse:
                        {
                            if (socket.AddressFamily == AddressFamily.InterNetworkV6)
                            {
                                try
                                {
                                    socket.SetSocketOption(SocketOptionLevel.IPv6, (SocketOptionName)27, true);
                                    socket.Bind(endpoint);
                                }
                                catch (SocketException e)
                                {
                                    if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Socket bind failed after setting dual mode with exception: " + e);
                                    return false;
                                }

                                return true;
                            }
                        }
                        break;
                    // Fixes Unity exception for iOS (requires IPv6 but the runtime throws)
                    case SocketError.AddressFamilyNotSupported:
                        {
                            return true;
                        }
                }

                if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Socket bind with exception: " + bindException);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Sends an unconnected message.
        /// </summary>
        /// <param name="payload">Payload.</param>
        /// <param name="endpoint">Endpoint.</param>
        public bool SendUnconnected(ArraySegment<byte> payload, IPEndPoint endpoint)
        {
            if (payload.Count > Config.MinimumMTU)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Error)  Logging.LogError("Tried to send unconnected message that was too large. [Size=" + payload.Count + "] [MaxMessageSize=" + Config.MinimumMTU + "]");
                return false;
            }

            // TODO: If payload has extra space. No need to realloc

            // Allocate the memory
            HeapMemory memory = MemoryManager.AllocHeapMemory((uint)payload.Count + 1);

            // Write headers
            memory.Buffer[0] = HeaderPacker.Pack(MessageType.UnconnectedData);

            // Copy payload to borrowed memory
            Buffer.BlockCopy(payload.Array, payload.Offset, memory.Buffer, 1, payload.Count);

            // Send the packet
            bool success = SendRaw(endpoint, new ArraySegment<byte>(memory.Buffer, (int)memory.VirtualOffset, (int)memory.VirtualCount));

            // Release memory
            MemoryManager.DeAlloc(memory);

            return success;
        }

        /// <summary>
        /// Sends a broadcast packet to all local devices.
        /// </summary>
        /// <returns><c>true</c>, if broadcast was sent, <c>false</c> otherwise.</returns>
        /// <param name="payload">The payload to send.</param>
        /// <param name="port">The port to send the broadcast to.</param>
        public bool SendBroadcast(ArraySegment<byte> payload, int port)
        {
            if (payload.Count > Config.MinimumMTU)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Tried to send broadcast message that was too large. [Size=" + payload.Count + "] [MaxMessageSize=" + Config.MinimumMTU + "]");
                return false;
            }

            bool broadcastSuccess = false;
            bool multicastSuccess = false;

            // TODO: If payload has extra space. No need to realloc

            // Alloc memory with space for header
            HeapMemory memory = MemoryManager.AllocHeapMemory((uint)(payload.Count + 1));

            // Write header
            memory.Buffer[0] = HeaderPacker.Pack(MessageType.Broadcast);

            // Copy payload
            Buffer.BlockCopy(payload.Array, payload.Offset, memory.Buffer, 1, payload.Count);

            try
            {
                if (_ipv4Socket != null)
                {
                    broadcastSuccess = _ipv4Socket.SendTo(memory.Buffer, (int)memory.VirtualOffset, (int)memory.VirtualCount, SocketFlags.None, new IPEndPoint(IPAddress.Broadcast, port)) > 0;
                }

                if (_ipv6Socket != null)
                {
                    multicastSuccess = _ipv6Socket.SendTo(memory.Buffer, (int)memory.VirtualOffset, (int)memory.VirtualCount, SocketFlags.None, new IPEndPoint(Constants.IPv6AllDevicesMulticastAddress, port)) > 0;
                }
            }
            catch (Exception e)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Error when sending broadcast: " + e);
            }

            // Release memory
            MemoryManager.DeAlloc(memory);

            return broadcastSuccess || multicastSuccess;
        }

        /// <summary>
        /// Starts a connection to a endpoint.
        /// This does the connection security logic on the calling thread. NOT the network thread. 
        /// If you have a high security connection, the solver will run on the caller thread.
        /// Note that this call will block the network thread and will cause slowdowns. Use ConnectLater for non blocking IO
        /// </summary>
        /// <returns>The pending connection.</returns>
        /// <param name="endpoint">The endpoint to connect to.</param>
        public Connection Connect(IPEndPoint endpoint)
        {
            if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogInfo("Attempting to connect to " + endpoint);

            ulong unixTimestamp = 0;
            ulong iv = 0;
            ulong additionsRequired = 0;

            if (Config.TimeBasedConnectionChallenge)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Using time based connection challenge. Calculating with difficulty " + Config.ChallengeDifficulty);

                // Current unix time
                unixTimestamp = (ulong)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;

                // Generate IV
                iv = RandomProvider.GetRandomULong();

                // Find collision
                if (HashCash.TrySolve(unixTimestamp + iv, (byte)Config.ChallengeDifficulty, ulong.MaxValue, out additionsRequired))
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Found hash collision after " + additionsRequired + " attempts. [IV=" + iv + "] [Time=" + unixTimestamp + "]");
                }
                else
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Failed to find hash collision. [IV=" + iv + "] [Time=" + unixTimestamp + "]");
                    return null;
                }
            }

            return ConnectInternal(endpoint, unixTimestamp, additionsRequired, iv);
        }

        private Connection ConnectInternal(IPEndPoint endpoint, ulong unixTimestamp, ulong counter, ulong iv)
        {
            Connection connection = AddNewConnection(endpoint, ConnectionState.RequestingConnection);

            if (connection != null)
            {
                // Set resend values
                connection.HandshakeResendAttempts = 1;
                connection.HandshakeLastSendTime = NetTime.Now;

                // Calculate the minimum size we could use for a packet
                int minSize = 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + (Config.TimeBasedConnectionChallenge ? sizeof(ulong) * 3 : 0);

                // Calculate the actual size with respect to amplification padding
                int size = Math.Max(minSize, (int)Config.AmplificationPreventionHandshakePadding);

                // Allocate the memory
                HeapMemory memory = MemoryManager.AllocHeapMemory((uint)size);

                // Set the header
                memory.Buffer[0] = HeaderPacker.Pack(MessageType.ConnectionRequest);

                // Copy the identification token
                Buffer.BlockCopy(Constants.RUFFLES_PROTOCOL_IDENTIFICATION, 0, memory.Buffer, 1, Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length);

                if (Config.TimeBasedConnectionChallenge)
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Using time based connection challenge. Writing solve with difficulty " + Config.ChallengeDifficulty);

                    // Save for resends
                    connection.PreConnectionChallengeTimestamp = unixTimestamp;

                    // Write the current unix time
                    for (byte i = 0; i < sizeof(ulong); i++) memory.Buffer[1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + i] = ((byte)(unixTimestamp >> (i * 8)));

                    // Save for resends
                    connection.PreConnectionChallengeCounter = counter;

                    // Write counter
                    for (byte i = 0; i < sizeof(ulong); i++) memory.Buffer[1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + sizeof(ulong) + i] = ((byte)(counter >> (i * 8)));

                    // Save for resends
                    connection.PreConnectionChallengeIV = iv;
                    
                    // Write IV
                    for (byte i = 0; i < sizeof(ulong); i++) memory.Buffer[1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + (sizeof(ulong) * 2) + i] = ((byte)(iv >> (i * 8)));

                    // Mark it as solved (for resending)
                    connection.PreConnectionChallengeSolved = true;
                }

                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Sending connection request to " + endpoint);

                // Send the packet
                connection.SendInternal(new ArraySegment<byte>(memory.Buffer, 0, (int)memory.VirtualCount), true);

                // Dealloc the memory
                MemoryManager.DeAlloc(memory);
            }
            else
            {
                if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Failed to allocate connection to " + endpoint);
            }

            return connection;
        }

        private void StartNetworkLogic()
        {
            Stopwatch logicWatch = new Stopwatch();
            logicWatch.Start();

            while (IsRunning)
            {
                try
                {
                    if (Simulator != null)
                    {
                        Simulator.RunLoop();
                    }

                    int elapsed = (int)logicWatch.ElapsedMilliseconds;

                    for (Connection connection = _headConnection; connection != null; connection = connection.NextConnection)
                    {
                        connection.Update();
                    }

                    int sleepMs = (Config.LogicDelay - (((int)logicWatch.ElapsedMilliseconds) - elapsed));

                    logicWatch.Reset();
                    logicWatch.Start();

                    if (sleepMs > 0)
                    {
                        Thread.Sleep(sleepMs);
                    }
                }
                catch (Exception e)
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Error when running internal loop: " + e);
                }
            }

            logicWatch.Stop();
        }

        private readonly IPEndPoint _fromIPv4Endpoint = new IPEndPoint(IPAddress.Any, 0);
        private readonly IPEndPoint _fromIPv6Endpoint = new IPEndPoint(IPAddress.IPv6Any, 0);

        private void StartSocketLogic()
        {
            // Only alloc buffer if we dont have any processing threads.
            byte[] _incomingBuffer = Config.ProcessingThreads > 0 ? null : new byte[Config.MaxBufferSize];
            List<Socket> _selectSockets = new List<Socket>();
            NetTime _lastLogicRun = NetTime.MinValue;
            int socketSelect = Config.LogicThreads <= 0 ? Config.LogicDelay * 1000 : 1000 * 1000;

            while (IsRunning)
            {
                _selectSockets.Clear();

                if (_ipv4Socket != null)
                {
                    _selectSockets.Add(_ipv4Socket);
                }

                if (_ipv6Socket != null)
                {
                    _selectSockets.Add(_ipv6Socket);
                }

                Socket.Select(_selectSockets, null, null, socketSelect);

                for (int i = 0; i < _selectSockets.Count; i++)
                {
                    try
                    {
                        // Get a endpoint reference
                        EndPoint _endpoint = _selectSockets[i].AddressFamily == AddressFamily.InterNetwork ? _fromIPv4Endpoint : _selectSockets[i].AddressFamily == AddressFamily.InterNetworkV6 ? _fromIPv6Endpoint : null;

                        byte[] receiveBuffer;
                        int receiveSize;
                        HeapMemory memory = null;

                        if (Config.ProcessingThreads > 0)
                        {
                            // Alloc memory for the packet. Alloc max MTU
                            memory = MemoryManager.AllocHeapMemory((uint)Config.MaximumMTU);
                            receiveSize = (int)memory.VirtualCount;
                            receiveBuffer = memory.Buffer;
                        }
                        else
                        {
                            receiveBuffer = _incomingBuffer;
                            receiveSize = _incomingBuffer.Length;
                        }

                        // Receive from socket
                        int size = _selectSockets[i].ReceiveFrom(receiveBuffer, 0, receiveSize, SocketFlags.None, ref _endpoint);

                        if (Config.ProcessingThreads > 0)
                        {
                            // Set the size to prevent reading to end
                            memory.VirtualCount = (uint)size;

                            // Process off thread
                            _processingQueue.Enqueue(new NetTuple<HeapMemory, IPEndPoint>(memory, (IPEndPoint)_endpoint));
                        }
                        else
                        {
                            // Process on thread
                            HandlePacket(new ArraySegment<byte>(receiveBuffer, 0, size), (IPEndPoint)_endpoint, true);
                        }
                    }
                    catch (SocketException e)
                    {
                        // TODO: Handle ConnectionReset and ConnectionRefused for Connect? More responsive?
                        // ConnectionReset and ConnectionRefused are triggered by local ICMP packets. Indicates remote is not present.
                        // MessageSize is triggered by remote ICMP. Usually during path MTU
                        if (e.SocketErrorCode != SocketError.ConnectionReset && e.SocketErrorCode != SocketError.ConnectionRefused && e.SocketErrorCode != SocketError.TimedOut && e.SocketErrorCode != SocketError.MessageSize)
                        {
                            if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Error when receiving from socket: " + e);
                        }
                    }
                    catch (Exception e)
                    {
                        if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Error when receiving from socket: " + e);
                    }
                }

                // If we have no logic thread. Run logic on receive thread
                if (Config.LogicThreads <= 0 && (NetTime.Now - _lastLogicRun).TotalMilliseconds > Config.LogicDelay)
                {
                    // Run logic

                    if (Simulator != null)
                    {
                        Simulator.RunLoop();
                    }

                    for (Connection connection = _headConnection; connection != null; connection = connection.NextConnection)
                    {
                        connection.Update();
                    }
                }
            }
        }

        private void StartPacketProcessing()
        {
            while (IsRunning)
            {
                EmptyPacketProcessingQueue();
            }
        }

        private void EmptyPacketProcessingQueue()
        {
            try
            {
                while (_processingQueue.TryDequeue(out NetTuple<HeapMemory, IPEndPoint> packet))
                {
                    // Process packet
                    HandlePacket(new ArraySegment<byte>(packet.Item1.Buffer, (int)packet.Item1.VirtualOffset, (int)packet.Item1.VirtualCount), packet.Item2, true);

                    // Dealloc the memory
                    MemoryManager.DeAlloc(packet.Item1);
                }
            }
            catch (Exception e)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Error when processing packet: " + e);
            }
        }

        /// <summary>
        /// Polls the RuffleSocket for incoming events about connections.
        /// </summary>
        /// <returns>The poll result.</returns>
        public NetworkEvent Poll()
        {
            if (_userEventQueue.TryDequeue(out NetworkEvent @event))
            {
                return @event;
            }

            return new NetworkEvent()
            {
                Connection = null,
                Socket = this,
                Data = new ArraySegment<byte>(),
                AllowUserRecycle = false,
                InternalMemory = null,
                Type = NetworkEventType.Nothing,
                ChannelId = 0,
                SocketReceiveTime = NetTime.Now,
                MemoryManager = MemoryManager,
                EndPoint = null
            };
        }

        internal bool SendRaw(IPEndPoint endpoint, ArraySegment<byte> payload)
        {
            try
            {
                if (endpoint.AddressFamily == AddressFamily.InterNetwork)
                {
                    return _ipv4Socket.SendTo(payload.Array, payload.Offset, payload.Count, SocketFlags.None, endpoint) > 0;
                }
                else if (endpoint.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    return _ipv6Socket.SendTo(payload.Array, payload.Offset, payload.Count, SocketFlags.None, endpoint) > 0;
                }
            }
            catch (SocketException e)
            {
                // MessageSize is ignored. This happens during path MTU

                if (e.SocketErrorCode != SocketError.MessageSize)
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Error when sending through socket: " + e);
                }
            }
            catch (Exception e)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Error when sending through socket: " + e);
            }

            return false;
        }

        private readonly List<ArraySegment<byte>> _mergeSegmentResults = new List<ArraySegment<byte>>();
        internal void HandlePacket(ArraySegment<byte> payload, IPEndPoint endpoint, bool allowMergeUnpack)
        {
            if (payload.Count < 1)
            {
                // Invalid size
                if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogError("Got packet of size " + payload.Count + " from " + endpoint + ". Packet is too small");
                return;
            }

            // Unpack header, dont cast to MessageType enum for safety
            HeaderPacker.Unpack(payload.Array[payload.Offset], out MessageType messageType);

            if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Unpacked packet. [MessageType=" + (MessageType)messageType + "]");

            switch (messageType)
            {
                case MessageType.Merge:
                    {
                        if (!Config.EnablePacketMerging)
                        {
                            // Big missmatch here.
                            if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Packet was merged but packet merging was disabled. Skipping merge packet");
                            return;
                        }

                        if (!allowMergeUnpack)
                        {
                            if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Packet was double merged. Skipping nested merge packet");
                            return;
                        }

                        Connection connection = GetConnection(endpoint);

                        if (connection != null)
                        {
                            // Unpack the merged packet
                            MessageMerger.Unpack(new ArraySegment<byte>(payload.Array, payload.Offset + 1, payload.Count - 1), _mergeSegmentResults);

                            if (_mergeSegmentResults != null)
                            {
                                for (int i = 0; i < _mergeSegmentResults.Count; i++)
                                {
                                    // Handle the segment
                                    HandlePacket(_mergeSegmentResults[i], endpoint, false);
                                }
                            }
                        }
                    }
                    break;
                case MessageType.ConnectionRequest:
                    {
                        if (payload.Count < Config.AmplificationPreventionHandshakePadding || payload.Count < 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length)
                        {
                            // This message is too small. They might be trying to use us for amplification.
                            return;
                        }

                        for (int i = 0; i < Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length; i++)
                        {
                            if (payload.Array[payload.Offset + 1 + i] != Constants.RUFFLES_PROTOCOL_IDENTIFICATION[i])
                            {
                                // The identification number did not match
                                if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Connection request packet was filtered away. Identification did not match");
                                return;
                            }
                        }

                        if (Config.TimeBasedConnectionChallenge)
                        {
                            // Get the current unix time seconds
                            ulong currentUnixTime = (ulong)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

                            // Read the time they used
                            ulong challengeUnixTime = (((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length]) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + 1] << 8) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + 2] << 16) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + 3] << 24) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + 4] << 32) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + 5] << 40) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + 6] << 48) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + 7] << 56));

                            // The seconds diff
                            long secondsDiff = (long)currentUnixTime - (long)challengeUnixTime;

                            if (secondsDiff > (long)Config.ConnectionChallengeTimeWindow || secondsDiff < -(long)Config.ConnectionChallengeTimeWindow)
                            {
                                // Outside the allowed window
                                if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogWarning("Client " + endpoint + " failed the connection request. They were outside of their allowed window. The diff was " + Math.Abs(secondsDiff) + " seconds");
                                return;
                            }

                            // Read the counter they used to collide the hash
                            ulong counter = (((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + sizeof(ulong)]) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + sizeof(ulong) + 1] << 8) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + sizeof(ulong) + 2] << 16) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + sizeof(ulong) + 3] << 24) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + sizeof(ulong) + 4] << 32) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + sizeof(ulong) + 5] << 40) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + sizeof(ulong) + 6] << 48) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + sizeof(ulong) + 7] << 56));

                            // Read the initialization vector they used
                            ulong userIv = (((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + (sizeof(ulong) * 2)]) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + (sizeof(ulong) * 2) + 1] << 8) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + (sizeof(ulong) * 2) + 2] << 16) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + (sizeof(ulong) * 2) + 3] << 24) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + (sizeof(ulong) * 2) + 4] << 32) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + (sizeof(ulong) * 2) + 5] << 40) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + (sizeof(ulong) * 2) + 6] << 48) |
                                            ((ulong)payload.Array[payload.Offset + 1 + Constants.RUFFLES_PROTOCOL_IDENTIFICATION.Length + (sizeof(ulong) * 2) + 7] << 56));

                            lock (_challengeInitializationVectorsLock)
                            {
                                // Ensure they dont reuse a IV
                                if (!_challengeInitializationVectors.TrySet(userIv))
                                {
                                    // This IV is being reused.
                                    if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogWarning("Client " + endpoint + " failed the connection request. They were trying to reuse an IV");
                                    return;
                                }
                            }

                            // Check if the hash collision they provided is valid
                            bool isValid = HashCash.Validate(challengeUnixTime + userIv, counter, (byte)Config.ChallengeDifficulty);

                            if (!isValid)
                            {
                                // They failed the challenge
                                if (Logging.CurrentLogLevel <= LogLevel.Info) Logging.LogWarning("Client " + endpoint + " failed the connection request. They submitted an invalid answer. [Counter=" + counter + "] [IV=" + userIv + "] [Time=" + challengeUnixTime + "]");
                                return;
                            }
                        }

                        if (Logging.CurrentLogLevel <= LogLevel.Debug) Logging.LogInfo("Client " + endpoint + " is being challenged");

                        Connection connection = AddNewConnection(endpoint, ConnectionState.RequestingChallenge);

                        if (connection != null)
                        {
                            // This connection was successfully added as pending

                            // Send connection request
                            connection.SendChallengeRequest();
                        }
                        else
                        {
                            if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Client " + endpoint + " could not be challenged. Allocation failed");
                        }
                    }
                    break;
                case MessageType.ChallengeRequest:
                    {
                        if (payload.Count < 10)
                        {
                            // The message is not large enough to contain all the data neccecary. Wierd server?
                            if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Server " + endpoint + " sent us a payload that was too small. Disconnecting");
                            return;
                        }

                        Connection connection = GetConnection(endpoint);

                        if (connection != null)
                        {
                            ulong challenge = (((ulong)payload.Array[payload.Offset + 1 + 0]) |
                                                ((ulong)payload.Array[payload.Offset + 1 + 1] << 8) |
                                                ((ulong)payload.Array[payload.Offset + 1 + 2] << 16) |
                                                ((ulong)payload.Array[payload.Offset + 1 + 3] << 24) |
                                                ((ulong)payload.Array[payload.Offset + 1 + 4] << 32) |
                                                ((ulong)payload.Array[payload.Offset + 1 + 5] << 40) |
                                                ((ulong)payload.Array[payload.Offset + 1 + 6] << 48) |
                                                ((ulong)payload.Array[payload.Offset + 1 + 7] << 56));

                            byte difficulty = payload.Array[payload.Offset + 1 + sizeof(ulong)];

                            connection.HandleChallengeRequest(challenge, difficulty);
                        }
                    }
                    break;
                case MessageType.ChallengeResponse:
                    {
                        if (payload.Count < Config.AmplificationPreventionHandshakePadding)
                        {
                            // This message is too small. They might be trying to use us for amplification
                            if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Client " + endpoint + " sent a challenge response that was smaller than the amplification padding");
                            return;
                        }

                        if (payload.Count < 9)
                        {
                            // The message is not large enough to contain all the data neccecary. Wierd client?
                            if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Client " + endpoint + " sent a challenge response that was too small to contain all the data");
                            return;
                        }

                        Connection connection = GetConnection(endpoint);

                        if (connection != null)
                        {
                            ulong challengeResponse = (((ulong)payload.Array[payload.Offset + 1 + 0]) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + 1] << 8) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + 2] << 16) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + 3] << 24) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + 4] << 32) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + 5] << 40) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + 6] << 48) |
                                                        ((ulong)payload.Array[payload.Offset + 1 + 7] << 56));

                            connection.HandleChallengeResponse(challengeResponse);
                        }
                    }
                    break;
                case MessageType.Hail:
                    {
                        Connection connection = GetConnection(endpoint);

                        if (connection != null)
                        {
                            if (payload.Count < 2)
                            {
                                // Invalid size.
                                if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogError("Client " + endpoint + " sent a payload that was too small");
                                return;
                            }

                            // Read the amount of channels
                            byte channelCount = payload.Array[payload.Offset + 1];

                            if (channelCount > Constants.MAX_CHANNELS)
                            {
                                // Too many channels
                                if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogError("Client " + endpoint + " more channels than allowed");
                                return;
                            }

                            if (payload.Count < channelCount + 2)
                            {
                                // Invalid size.
                                if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogError("Client " + endpoint + " sent a payload that was too small");
                                return;
                            }

                            // Read the types and validate them (before alloc)
                            for (byte i = 0; i < channelCount; i++)
                            {
                                byte channelType = payload.Array[payload.Offset + 2 + i];

                                if (!ChannelTypeUtils.IsValidChannelType(channelType))
                                {
                                    // Unknown channel type. Disconnect.
                                    if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogError("Client " + endpoint + " sent an invalid ChannelType");
                                    return;
                                }
                            }

                            connection.HandleHail(new ArraySegment<byte>(payload.Array, payload.Offset + 2, payload.Array[payload.Offset + 1]));
                        }
                    }
                    break;
                case MessageType.HailConfirmed:
                    {
                        Connection connection = GetConnection(endpoint);

                        if (connection != null)
                        {
                            connection.HandleHailConfirmed();
                        }
                    }
                    break;
                case MessageType.Heartbeat:
                    {
                        if (!Config.EnableHeartbeats)
                        {
                            // This is a missmatch.
                            if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Heartbeat received from " + endpoint + " but the we do not have heartbeats enabled. Configuration missmatch?");
                            return;
                        }

                        Connection connection = GetConnection(endpoint);

                        if (connection != null)
                        {
                            // Heartbeats are sequenced to not properly handle network congestion
                            connection.HandleHeartbeat(new ArraySegment<byte>(payload.Array, payload.Offset + 1, payload.Count - 1));
                        }
                    }
                    break;
                case MessageType.Data:
                    {
                        Connection connection = GetConnection(endpoint);

                        if (connection != null)
                        {
                            connection.HandleChannelData(new ArraySegment<byte>(payload.Array, payload.Offset + 1, payload.Count - 1));
                        }
                    }
                    break;
                case MessageType.Ack:
                    {
                        Connection connection = GetConnection(endpoint);

                        if (connection != null)
                        {
                            connection.HandleChannelAck(new ArraySegment<byte>(payload.Array, payload.Offset + 1, payload.Count - 1));
                        }
                    }
                    break;
                case MessageType.Disconnect:
                    {
                        Connection connection = GetConnection(endpoint);

                        if (connection != null)
                        {
                            connection.DisconnectInternal(false, false);
                        }
                    }
                    break;
                case MessageType.MTURequest:
                    {
                        if (!Config.EnablePathMTU)
                        {
                            if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Got MTURequest message but SocketConfig.EnablePathMTU is disabled.");
                            return;
                        }

                        Connection connection = GetConnection(endpoint);

                        if (connection != null)
                        {
                            connection.HandleMTURequest((uint)payload.Count);
                        }
                    }
                    break;
                case MessageType.MTUResponse:
                    {
                        if (!Config.EnablePathMTU)
                        {
                            if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Got MTUResponse message but SocketConfig.EnablePathMTU is disabled.");
                            return;
                        }

                        Connection connection = GetConnection(endpoint);

                        if (connection != null)
                        {
                            connection.HandleMTUResponse((uint)payload.Count);
                        }
                    }
                    break;
                case MessageType.UnconnectedData:
                    {
                        if (!Config.AllowUnconnectedMessages)
                        {
                            if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Got unconnected message but SocketConfig.AllowUnconnectedMessages is false.");
                            return;
                        }

                        // Alloc memory that can be borrowed to userspace
                        HeapMemory memory = MemoryManager.AllocHeapMemory((uint)payload.Count - 1);

                        // Copy payload to borrowed memory
                        Buffer.BlockCopy(payload.Array, payload.Offset + 1, memory.Buffer, 0, payload.Count - 1);

                        // Send to userspace
                        PublishEvent(new NetworkEvent()
                        {
                            Connection = null,
                            Socket = this,
                            Type = NetworkEventType.UnconnectedData,
                            AllowUserRecycle = true,
                            Data = new ArraySegment<byte>(memory.Buffer, (int)memory.VirtualOffset, (int)memory.VirtualCount),
                            InternalMemory = memory,
                            SocketReceiveTime = NetTime.Now,
                            ChannelId = 0,
                            MemoryManager = MemoryManager,
                            EndPoint = endpoint
                        });
                    }
                    break;
                case MessageType.Broadcast:
                    {
                        if (!Config.AllowBroadcasts)
                        {
                            if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Got broadcast message but SocketConfig.AllowBroadcasts is false.");
                            return;
                        }

                        // Alloc memory that can be borrowed to userspace
                        HeapMemory memory = MemoryManager.AllocHeapMemory((uint)payload.Count - 1);

                        // Copy payload to borrowed memory
                        Buffer.BlockCopy(payload.Array, payload.Offset + 1, memory.Buffer, 0, payload.Count - 1);

                        // Send to userspace
                        PublishEvent(new NetworkEvent()
                        {
                            Connection = null,
                            Socket = this,
                            Type = NetworkEventType.BroadcastData,
                            AllowUserRecycle = true,
                            Data = new ArraySegment<byte>(memory.Buffer, (int)memory.VirtualOffset, (int)memory.VirtualCount),
                            InternalMemory = memory,
                            SocketReceiveTime = NetTime.Now,
                            ChannelId = 0,
                            MemoryManager = MemoryManager,
                            EndPoint = endpoint
                        });
                    }
                    break;

            }
        }

        internal Connection GetConnection(IPEndPoint endpoint)
        {
            // Lock to prevent grabbing a half dead connection
            _connectionsLock.EnterReadLock();

            try
            {
                if (_addressConnectionLookup.ContainsKey(endpoint))
                {
                    return _addressConnectionLookup[endpoint];
                }
                else
                {
                    return null;
                }
            }
            finally
            {
                _connectionsLock.ExitReadLock();
            }
        }

        internal void RemoveConnection(Connection connection)
        {
            // Lock when removing the connection to prevent another thread grabbing it before its fully dead.
            _connectionsLock.EnterWriteLock();

            try
            {
                // Remove lookup
                if (_addressConnectionLookup.Remove(connection.EndPoint))
                {
                    if (connection == _headConnection)
                    {
                        _headConnection = _headConnection.NextConnection;
                    }

                    if (connection.PreviousConnection != null)
                    {
                        connection.PreviousConnection.NextConnection = connection.NextConnection;
                    }

                    if (connection.NextConnection != null)
                    {
                        connection.NextConnection.PreviousConnection = connection.PreviousConnection;
                    }

                    connection.PreviousConnection = null;

                    if (((ulong)_releasedConnectionIds.Count + 1) == _connectionIdCounter)
                    {
                        // If the counter is equal to the amount of released + 1. This means all have been released and we can safely clear the queue and reset the counter.
                        // This is a meassure to save some memory. 
                        // TODO: Improve: If 100 connections join, the last 99 leave. The queue will have 99 entries and the counter will be 100. It would be better if the counter was decreased to 1 and the 99 entries dropped.

                        _connectionIdCounter = 0;
                        _releasedConnectionIds.Clear();
                    }
                    else if (_connectionIdCounter == connection.Id + 1)
                    {
                        // This was the last connection to be added. Instead of enqueueing, we can just decrease the counter
                        _connectionIdCounter--;
                    }
                    else
                    {
                        // Add the released connectionId
                        _releasedConnectionIds.Enqueue(connection.Id);
                    }
                }
                else
                {
                    if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Could not find endpoint to remove: " + connection.EndPoint);
                }
            }
            finally
            {
                _connectionsLock.ExitWriteLock();
            }
        }

        internal Connection AddNewConnection(IPEndPoint endpoint, ConnectionState state)
        {
            // Lock when adding connection to prevent grabbing a half alive connection.
            _connectionsLock.EnterWriteLock();

            try
            {
                // Make sure they are not already connected to prevent an attack where a single person can fill all the slots.
                if (_addressConnectionLookup.ContainsKey(endpoint))
                {
                    return null;
                }

                // Get a connectionId
                ulong connectionId;
                if (_releasedConnectionIds.Count > 0)
                {
                    connectionId = _releasedConnectionIds.Dequeue();
                }
                else
                {
                    connectionId = _connectionIdCounter++;
                }

                // Alloc on the heap
                Connection connection = new Connection(connectionId, state, endpoint, this);

                // Add lookup
                _addressConnectionLookup.Add(endpoint, connection);

                if (_headConnection != null)
                {
                    // We have a connection as head.
                    connection.NextConnection = _headConnection;
                    _headConnection.PreviousConnection = connection;
                }

                _headConnection = connection;

                return connection;
            }
            finally
            {
                _connectionsLock.ExitWriteLock();
            }
        }
    }
}
