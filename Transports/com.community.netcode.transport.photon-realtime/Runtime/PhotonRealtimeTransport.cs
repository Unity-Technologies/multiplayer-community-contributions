using ExitGames.Client.Photon;
using Photon.Realtime;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace Netcode.Transports.PhotonRealtime
{
    [DefaultExecutionOrder(-1000)]
    public partial class PhotonRealtimeTransport : NetworkTransport, IOnEventCallback
    {
        private static readonly ArraySegment<byte> s_EmptyArraySegment = new ArraySegment<byte>(Array.Empty<byte>());

        [Tooltip("The nickname of the player in the Photon Room. This value is only relevant for other Photon Realtime features. Leaving it empty generates a random name.")]
        [SerializeField]
        private string m_NickName;

        [Header("Server Settings")]
        [Tooltip("Unique name of the room for this session.")]
        [SerializeField]
        private string m_RoomName;

        [Tooltip("The maximum amount of players allowed in the room.")]
        [SerializeField]
        private byte m_MaxPlayers = 16;

        [FormerlySerializedAs("m_ChannelIdCodesStartRange")]
        [Header("Advanced Settings")]
        [Tooltip("The first byte of the range of photon event codes which this transport will reserve for unbatched messages. Should be set to a number lower then 200 to not interfere with photon internal events. Approximately 8 events will be reserved.")]
        [SerializeField]
        private byte m_NetworkDeliveryEventCodesStartRange = 0;

        [Tooltip("Attaches the photon support logger to the transport. Useful for debugging disconnects or other issues.")]
        [SerializeField]
        private bool m_AttachSupportLogger = false;

        [Tooltip("The batching this transport should apply to MLAPI events. None only works for very simple scenes.")]
        [SerializeField]
        private BatchMode m_BatchMode = BatchMode.SendAllReliable;

        [Tooltip("The maximum size of the send queue which batches MLAPI events into Photon events.")]
        [SerializeField]
        private int m_SendQueueBatchSize = 4096;

        [Tooltip("The Photon event code which will be used to send batched data.")]
        [SerializeField]
        [Range(129, 199)]
        private byte m_BatchedTransportEventCode = 129;

        [Tooltip("The Photon event code which will be used to send a kick.")]
        [SerializeField]
        [Range(129, 199)]
        private byte m_KickEventCode = 130;

        private LoadBalancingClient m_Client;

        private bool m_IsHostOrServer;

        /// <summary>
        /// SendQueue dictionary is used to batch events instead of sending them immediately.
        /// </summary>
        private readonly Dictionary<SendTarget, SendQueue> m_SendQueue = new Dictionary<SendTarget, SendQueue>();

        /// <summary>
        /// This exists to cache raise event options when calling <see cref="RaisePhotonEvent"./> Saves us from 2 allocations per send.
        /// </summary>
        private RaiseEventOptions m_CachedRaiseEventOptions = new RaiseEventOptions() { TargetActors = new int[1] };

        /// <summary>
        /// Limits the number of datagrams sent in a single frame.
        /// </summary>
        private const int MAX_DGRAM_PER_FRAME = 4;

        /// <summary>
        /// Gets or sets the room name to create or join.
        /// </summary>
        public string RoomName
        {
            get => m_RoomName;
            set => m_RoomName = value;
        }

        /// <summary>
        /// The Photon loadbalancing client used by this transport for everything networking related.
        /// </summary>
        public LoadBalancingClient Client => m_Client;

        ///<inheritdoc/>
        public override ulong ServerClientId => GetMlapiClientId(0, true);

        // -------------- MonoBehaviour Handlers --------------------------------------------------------------------------

        /// <summary>
        /// In Update before other scripts run we dispatch incoming commands.
        /// </summary>
        void Update()
        {
            if (m_Client != null)
            {
                do { } while (m_Client.LoadBalancingPeer.DispatchIncomingCommands());
            }
        }

        /// <summary>
        /// Send batched messages out in LateUpdate.
        /// </summary>
        void LateUpdate()
        {
            if (m_Client != null)
            {
                FlushAllSendQueues();

                for (int i = 0; i < MAX_DGRAM_PER_FRAME; i++)
                {
                    bool anythingLeftToSend = m_Client.LoadBalancingPeer.SendOutgoingCommands();
                    if (!anythingLeftToSend)
                    {
                        break;
                    }
                }
            }
        }

        // -------------- Transport Utils -----------------------------------------------------------------------------

        /// <summary>
        /// Create and Initialize the internal LoadBalancingClient used to relay data with Photon Cloud
        /// </summary>
        private void InitializeClient()
        {
            if (m_Client == null)
            {
                // This is taken from a Photon Realtime sample to get a random user name if none is provided.
                var nickName = string.IsNullOrEmpty(m_NickName) ? "usr" + SupportClass.ThreadSafeRandom.Next() % 99 : m_NickName;

                m_Client = new LoadBalancingClient
                {
                    LocalPlayer = { NickName = nickName },
                };

                // Register callbacks
                m_Client.AddCallbackTarget(this);

                // these two settings enable (almost) zero alloc sending and receiving of byte[] content
                m_Client.LoadBalancingPeer.ReuseEventInstance = true;
                m_Client.LoadBalancingPeer.UseByteArraySlicePoolForEvents = true;

                // Attach Logger
                if (m_AttachSupportLogger)
                {
                    var logger = gameObject.GetComponent<SupportLogger>() ?? gameObject.AddComponent<SupportLogger>();
                    logger.Client = m_Client;
                }
            }
        }

        /// <summary>
        /// Creates and connects a peer synchronously to the region master server and returns a bool containing the result.
        /// </summary>
        /// <returns></returns>
        private bool ConnectPeer()
        {
            InitializeClient();

            return m_Client.ConnectUsingSettings(PhotonAppSettings.Instance.AppSettings);
        }

        // -------------- Send/Receive --------------------------------------------------------------------------------

        ///<inheritdoc/>
        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery networkDelivery)
        {
            var isReliable = DeliveryModeToReliable(networkDelivery);

            if (m_BatchMode == BatchMode.None)
            {
                RaisePhotonEvent(clientId, isReliable, data, (byte)(m_NetworkDeliveryEventCodesStartRange + networkDelivery));
                return;
            }

            SendQueue queue;
            SendTarget sendTarget = new SendTarget(clientId, isReliable);

            if (m_BatchMode == BatchMode.SendAllReliable)
            {
                sendTarget.IsReliable = true;
            }

            if (!m_SendQueue.TryGetValue(sendTarget, out queue))
            {
                queue = new SendQueue(m_SendQueueBatchSize);
                m_SendQueue.Add(sendTarget, queue);
            }

            if (!queue.AddEvent(data))
            {
                // If we are in here data exceeded remaining queue size. This should not happen under normal operation.
                if (data.Count > queue.Size)
                {
                    // If data is too large to be batched, flush it out immediately. This happens with large initial spawn packets from MLAPI.
                    Debug.LogWarning($"Sent {data.Count} bytes on NetworkDelivery: {networkDelivery}. Event size exceeds sendQueueBatchSize: ({m_SendQueueBatchSize}).");
                    RaisePhotonEvent(sendTarget.ClientId, sendTarget.IsReliable, data, (byte)(m_NetworkDeliveryEventCodesStartRange + networkDelivery));
                }
                else
                {
                    var sendBuffer = queue.GetData();
                    RaisePhotonEvent(sendTarget.ClientId, sendTarget.IsReliable, sendBuffer, m_BatchedTransportEventCode);
                    queue.Clear();
                    queue.AddEvent(data);
                }
            }
        }

        /// <summary>
        /// Flushes all send queues. (Raises photon events with data from their buffers and clears them)
        /// </summary>
        private void FlushAllSendQueues()
        {
            foreach (var kvp in m_SendQueue)
            {
                if (kvp.Value.IsEmpty()) continue;

                var sendBuffer = kvp.Value.GetData();
                RaisePhotonEvent(kvp.Key.ClientId, kvp.Key.IsReliable, sendBuffer, m_BatchedTransportEventCode);
                kvp.Value.Clear();
            }
        }

        /// <summary>
        /// Send an event using the LoadBalancingClient to an specific client
        /// </summary>
        /// <param name="clientId">Target Client to send the event</param>
        /// <param name="isReliable">Signal if this event must be sent in Reliable Mode</param>
        /// <param name="data">Data to be send</param>
        /// <param name="eventCode">Event Code ID</param>
        private void RaisePhotonEvent(ulong clientId, bool isReliable, ArraySegment<byte> data, byte eventCode)
        {
            if (m_Client == null || !m_Client.InRoom)
            {
                // the local client is set to null or it's not in a room. can't send events, so it makes sense to disconnect MLAPI layer.
                this.InvokeTransportEvent(NetworkEvent.Disconnect);
                return;
            }

            m_CachedRaiseEventOptions.TargetActors[0] = GetPhotonRealtimeId(clientId);
            var sendOptions = isReliable ? SendOptions.SendReliable : SendOptions.SendUnreliable;

            // This allocates because data gets boxed to object.
            m_Client.OpRaiseEvent(eventCode, data, m_CachedRaiseEventOptions, sendOptions);
        }

        // -------------- Transport Handlers --------------------------------------------------------------------------

        ///<inheritdoc/>
        public override void Initialize(NetworkManager networkManager = null) { }

        ///<inheritdoc/>
        public override void Shutdown()
        {
            if (m_Client != null && m_Client.IsConnected)
            {
                m_Client.Disconnect();
            }
            else
            {
                this.DeInitialize();
            }
        }

        ///<inheritdoc/>
        public override bool StartClient()
        {
            bool connected = ConnectPeer();
            return connected;
        }

        ///<inheritdoc/>
        public override bool StartServer()
        {
            var result = ConnectPeer();
            if (result == false)
            {
                return false;
            }

            m_IsHostOrServer = true;
            return true;
        }

        /// <summary>
        /// Photon Realtime Transport is event based. Polling will always return nothing.
        /// </summary>
        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0;
            receiveTime = Time.realtimeSinceStartup;
            payload = default;
            return NetworkEvent.Nothing;
        }

        ///<inheritdoc/>
        public override ulong GetCurrentRtt(ulong clientId)
        {
            // This is only an approximate value based on the own client's rtt to the server and could cause issues, maybe use a similar approach as the Steamworks transport.
            return (ulong)(m_Client.LoadBalancingPeer.RoundTripTime * 2);
        }

        ///<inheritdoc/>
        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (this.m_Client != null && m_Client.InRoom && this.m_Client.LocalPlayer.IsMasterClient)
            {
                ArraySegment<byte> payload = s_EmptyArraySegment;
                RaisePhotonEvent(clientId, true, payload, this.m_KickEventCode);
            }
        }

        ///<inheritdoc/>
        public override void DisconnectLocalClient()
        {
            this.Shutdown();
        }

        // -------------- Event Handlers ------------------------------------------------------------------------------

        /// <summary>
        /// LBC Event Handler
        /// </summary>
        /// <param name="eventData">Event Data</param>
        public void OnEvent(EventData eventData)
        {
            if (eventData.Code >= 200) { return; } // EventCode is a photon event.

            var senderId = GetMlapiClientId(eventData.Sender, false);

            // handle kick
            if (eventData.Code == this.m_KickEventCode)
            {
                if (this.m_Client.InRoom && eventData.Sender == m_originalRoomMasterClient)
                {
                    InvokeTransportEvent(NetworkEvent.Disconnect, senderId);
                }

                return;
            }

            // handle data
            using (ByteArraySlice slice = eventData.CustomData as ByteArraySlice)
            {
                if (slice == null)
                {
                    Debug.LogError("Photon option UseByteArraySlicePoolForEvents should be set to true.");
                    return;
                }

                if (eventData.Code == this.m_BatchedTransportEventCode)
                {
                    var segment = new ArraySegment<byte>(slice.Buffer, slice.Offset, slice.Count);
                    using var reader = new FastBufferReader(segment, Allocator.Temp);
                    while (reader.Position < segment.Count) // TODO Not using reader.Lenght here becaues it's broken: https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/issues/1310
                    {
                        reader.ReadValueSafe(out int length);
                        byte[] dataArray = new byte[length];
                        reader.ReadBytesSafe(ref dataArray, length);

                        InvokeTransportEvent(NetworkEvent.Data, senderId, new ArraySegment<byte>(dataArray, 0, dataArray.Length));
                    }
                }
                else
                {
                    // Event is a non-batched data event.
                    ArraySegment<byte> payload = new ArraySegment<byte>(slice.Buffer, slice.Offset, slice.Count);

                    InvokeTransportEvent(NetworkEvent.Data, senderId, payload);
                }
            }
        }

        // -------------- Utility Methods -----------------------------------------------------------------------------

        /// <summary>
        /// Invoke Transport Events.
        /// </summary>
        /// <param name="networkEvent">Network Event Type</param>
        /// <param name="senderId">Peer Sender ID</param>
        /// <param name="payload">Event Payload</param>
        private void InvokeTransportEvent(NetworkEvent networkEvent, ulong senderId = 0, ArraySegment<byte> payload = default)
        {
            switch (networkEvent)
            {
                case NetworkEvent.Nothing:
                    // do nothing
                    break;
                case NetworkEvent.Disconnect:
                    if (m_IsHostOrServer && ServerClientId == senderId)
                    {
                        ForceStopPeer();
                    }

                    goto default;
                default:
                    InvokeOnTransportEvent(networkEvent, senderId, payload, Time.realtimeSinceStartup);
                    break;
            }
        }

        /// <summary>
        /// Convert the <see cref="Unity.Netcode.NetworkDelivery"/> to a bool indicating whether the
        /// <see cref="PhotonRealtimeTransport"/> should use the reliable or unreliable sendmode./>
        /// </summary>
        /// <param name="deliveryMode">Delivery mode to convert</param>
        /// <returns>A bool indicating whether this delivery is reliable.</returns>
        private bool DeliveryModeToReliable(NetworkDelivery deliveryMode)
        {
            switch (deliveryMode)
            {
                case NetworkDelivery.Unreliable:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Convert a Photon Client ID to MLAPI Client ID
        /// </summary>
        /// <param name="photonId">Photon ID</param>
        /// <param name="isServer">Flag if running on server</param>
        /// <returns>MLAPI Client ID</returns>
        private ulong GetMlapiClientId(int photonId, bool isServer)
        {
            if (isServer)
            {
                return 0;
            }
            else
            {
                return (ulong)(photonId + 1);
            }
        }

        /// <summary>
        /// Convert MLAPI Client ID to Photon Client ID
        /// </summary>
        /// <param name="clientId">MLAPI Client ID to convert</param>
        /// <returns>Photon Client ID</returns>
        private int GetPhotonRealtimeId(ulong clientId)
        {
            if (clientId == 0)
            {
                return CurrentMasterId;
            }
            else
            {
                return (int)(clientId - 1);
            }
        }

        /// <summary>
        /// Reset all member properties to a blank state for later reuse.
        /// </summary>
        private void DeInitialize()
        {
            m_originalRoomMasterClient = -1;
            m_IsHostOrServer = false;
            m_Client?.RemoveCallbackTarget(this);
            m_Client = null;
        }

        /// <summary>
        /// Force the Local Peer to Stop
        /// </summary>
        private void ForceStopPeer()
        {
            if (NetworkManager.Singleton == null) { return; }

            NetworkManager.Singleton.Shutdown();
        }

        // -------------- Utility Types -------------------------------------------------------------------------------

        /// <summary>
        /// Memory Stream controller to store several events into one single buffer
        /// </summary>
        class SendQueue : IDisposable
        {
            FastBufferWriter m_Writer;

            /// <summary>
            /// The size of the send queue.
            /// </summary>
            public int Size { get; }

            public SendQueue(int size)
            {
                Size = size;
                m_Writer = new FastBufferWriter(size, Allocator.Persistent);
            }

            /// <summary>
            /// Ads an event to the send queue.
            /// </summary>
            /// <param name="data">The data to send.</param>
            /// <returns>True if the event was added successfully to the queue. False if there was no space in the queue.</returns>
            internal bool AddEvent(ArraySegment<byte> data)
            {
                if (m_Writer.TryBeginWrite(data.Count + 4) == false)
                {
                    return false;
                }

                m_Writer.WriteValue(data.Count);
                m_Writer.WriteBytes(data.Array, data.Count, data.Offset);

                return true;
            }

            internal void Clear()
            {
                m_Writer.Truncate(0);
            }

            internal bool IsEmpty()
            {
                return m_Writer.Position == 0;
            }

            internal ArraySegment<byte> GetData()
            {
                var array = m_Writer.ToArray();
                return new ArraySegment<byte>(array);
            }

            public void Dispose()
            {
                m_Writer.Dispose();
            }
        }

        /// <summary>
        /// Cached information about reliability mode with a certain client
        /// </summary>
        struct SendTarget
        {
            public ulong ClientId;
            public bool IsReliable;

            public SendTarget(ulong clientId, bool isReliable)
            {
                ClientId = clientId;
                IsReliable = isReliable;
            }
        }

        /// <summary>
        /// Batch Mode used by the MLAPI Events when sending to another clients
        /// </summary>
        enum BatchMode : byte
        {
            /// <summary>
            /// The transport performs no batching.
            /// </summary>
            None = 0,
            /// <summary>
            /// Batches all MLAPI events into reliable sequenced messages.
            /// </summary>
            SendAllReliable = 1,
            /// <summary>
            /// Batches all reliable MLAPI events into a single photon event and all unreliable MLAPI events into an unreliable photon event.
            /// </summary>
            ReliableAndUnreliable = 2,
        }
    }
}
