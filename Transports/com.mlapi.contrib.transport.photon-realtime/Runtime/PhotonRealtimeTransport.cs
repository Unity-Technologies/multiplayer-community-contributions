using ExitGames.Client.Photon;
using MLAPI.Serialization.Pooled;
using MLAPI.Transports.Tasks;
using Photon.Realtime;
using System;
using System.Collections.Generic;
using System.IO;
using MLAPI.Logging;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Assertions;

namespace MLAPI.Transports.PhotonRealtime
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

        [Header("Advanced Settings")]
        [Tooltip("The first byte of the range of photon event codes which this transport will reserve for unbatched messages. Should be set to a number lower then 200 to not interfere with photon internal events. Approximately 8 events will be reserved.")]
        [SerializeField]
        private byte m_ChannelIdCodesStartRange = 0;

        [Tooltip("Attaches the photon support logger to the transport. Useful for debugging disconnects or other issues.")]
        [SerializeField]
        private bool m_AttachSupportLogger = false;

        [Tooltip("The batching this transport should apply to MLAPI events. None only works for very simple scenes.")]
        [SerializeField]
        private BatchMode m_BatchMode = BatchMode.SendAllReliable;

        [Tooltip("The maximum size of the send queue which batches MLAPI events into Photon events.")]
        [SerializeField]
        private int m_SendQueueBatchSize = 4096;

        [Tooltip("The Photon event code which will be used to send batched data over MLAPI channels.")]
        [SerializeField]
        [Range(129, 199)]
        private byte m_BatchedTransportEventCode = 129;

        [Tooltip("The Photon event code which will be used to send a kick.")]
        [SerializeField]
        [Range(129, 199)]
        private byte m_KickEventCode = 130;

        private SocketTask m_ConnectTask;
        private LoadBalancingClient m_Client;

        private bool m_IsHostOrServer;

        private readonly Dictionary<NetworkChannel, byte> m_ChannelToId = new Dictionary<NetworkChannel, byte>();
        private readonly Dictionary<byte, NetworkChannel> m_IdToChannel = new Dictionary<byte, NetworkChannel>();
        private readonly Dictionary<ushort, RealtimeChannel> m_Channels = new Dictionary<ushort, RealtimeChannel>();

        /// <summary>
        /// SendQueue dictionary is used to batch events instead of sending them immediately.
        /// </summary>
        private readonly Dictionary<SendTarget, SendQueue> m_SendQueue = new Dictionary<SendTarget, SendQueue>();

        /// <summary>
        /// This exists to cache raise event options when calling <see cref="RaisePhotonEvent"./> Saves us from 2 allocations per send.
        /// </summary>
        private RaiseEventOptions m_CachedRaiseEventOptions = new RaiseEventOptions() { TargetActors = new int[1] };

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
            FlushAllSendQueues();

            if (m_Client != null)
            {
                do { } while (m_Client.LoadBalancingPeer.SendOutgoingCommands());
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
                var nickName = string.IsNullOrEmpty(m_NickName) ? m_NickName : "usr" + SupportClass.ThreadSafeRandom.Next() % 99;

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
        /// Creates and connects a peer synchronously to the region master server and returns a <see cref="SocketTask"/> containing the result.
        /// </summary>
        /// <returns></returns>
        private SocketTask ConnectPeer()
        {
            m_ConnectTask = SocketTask.Working;
            InitializeClient();

            var connected = m_Client.ConnectUsingSettings(PhotonAppSettings.Instance.AppSettings);

            if (!connected)
            {
                m_ConnectTask = SocketTask.Fault;
                m_ConnectTask.Message = $"Can't connect to region: {this.m_Client.CloudRegion}";
            }

            return m_ConnectTask;
        }

        // -------------- Send/Receive --------------------------------------------------------------------------------

        ///<inheritdoc/>
        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkChannel networkChannel)
        {
            RealtimeChannel channel = m_Channels[m_ChannelToId[networkChannel]];

            if (m_BatchMode == BatchMode.None)
            {
                RaisePhotonEvent(clientId, channel.SendMode.Reliability, data, channel.Id);
                return;
            }

            SendQueue queue;
            SendTarget sendTarget = new SendTarget(clientId, channel.SendMode.Reliability);

            if (m_BatchMode == BatchMode.SendAllReliable)
            {
                sendTarget.IsReliable = true;
            }

            if (!m_SendQueue.TryGetValue(sendTarget, out queue))
            {
                queue = new SendQueue(m_SendQueueBatchSize);
                m_SendQueue.Add(sendTarget, queue);
            }

            if (!queue.AddEvent(channel.Id, data))
            {
                // If we are in here data exceeded remaining queue size. This should not happen under normal operation.
                if (data.Count > queue.Size)
                {
                    // If data is too large to be batched, flush it out immediately. This happens with large initial spawn packets from MLAPI.
                    Debug.LogWarning($"Sent {data.Count} bytes on channel: {networkChannel}. Event size exceeds sendQueueBatchSize: ({m_SendQueueBatchSize}).");
                    RaisePhotonEvent(sendTarget.ClientId, sendTarget.IsReliable, data, channel.Id);
                }
                else
                {
                    var sendBuffer = queue.GetData();
                    RaisePhotonEvent(sendTarget.ClientId, sendTarget.IsReliable, sendBuffer, m_BatchedTransportEventCode);
                    queue.Clear();
                    queue.AddEvent(channel.Id, data);
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
            m_CachedRaiseEventOptions.TargetActors[0] = GetPhotonRealtimeId(clientId);
            var sendOptions = isReliable ? SendOptions.SendReliable : SendOptions.SendUnreliable;

            // This allocates because data gets boxed to object.
            m_Client.OpRaiseEvent(eventCode, data, m_CachedRaiseEventOptions, sendOptions);
        }

        // -------------- Transport Handlers --------------------------------------------------------------------------

        ///<inheritdoc/>
        public override void Init()
        {
            for (byte i = 0; i < MLAPI_CHANNELS.Length; i++)
            {
                byte channelID = (byte)(i + m_ChannelIdCodesStartRange);
                NetworkChannel channel = MLAPI_CHANNELS[i].Channel;

                m_IdToChannel.Add(channelID, channel);
                m_ChannelToId.Add(channel, channelID);
                m_Channels.Add(channelID, new RealtimeChannel()
                {
                    Id = channelID,
                    SendMode = MlapiChannelTypeToSendOptions(MLAPI_CHANNELS[i].Delivery)
                });
            }
        }

        ///<inheritdoc/>
        public override void Shutdown()
        {
            if (m_Client != null && m_Client.IsConnected)
            {
                m_Client.Disconnect();
            }
            else
            {
                this.DeInit();
            }
        }

        ///<inheritdoc/>
        public override SocketTasks StartClient()
        {
            return ConnectPeer().AsTasks();
        }

        ///<inheritdoc/>
        public override SocketTasks StartServer()
        {
            var task = ConnectPeer();
            m_IsHostOrServer = true;
            return task.AsTasks();
        }

        /// <summary>
        /// Photon Realtime Transport is event based. Polling will always return nothing.
        /// </summary>
        public override NetworkEvent PollEvent(out ulong clientId, out NetworkChannel channel, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0;
            channel = default;
            receiveTime = Time.realtimeSinceStartup;
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
            if (m_Client.InRoom && this.m_Client.LocalPlayer.IsMasterClient)
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
                    using (PooledNetworkBuffer buffer = PooledNetworkBuffer.Get())
                    {
                        // moving data from one pooled wrapper to another (for MLAPI to read incoming data)
                        buffer.Position = 0;
                        buffer.Write(slice.Buffer, slice.Offset, slice.Count);
                        buffer.SetLength(slice.Count);
                        buffer.Position = 0;

                        using (PooledNetworkReader reader = PooledNetworkReader.Get(buffer))
                        {
                            while (buffer.Position < buffer.Length)
                            {
                                byte channelId = reader.ReadByteDirect();
                                int length = reader.ReadInt32Packed();
                                byte[] dataArray = reader.ReadByteArray(null, length);

                                InvokeTransportEvent(NetworkEvent.Data, senderId, m_IdToChannel[channelId], new ArraySegment<byte>(dataArray, 0, dataArray.Length));
                            }
                        }
                    }

                    return;
                }
                else
                {
                    // Event is a non-batched data event.
                    ArraySegment<byte> payload = new ArraySegment<byte>(slice.Buffer, slice.Offset, slice.Count);
                    NetworkChannel channel = m_IdToChannel[eventData.Code];
                        
                    InvokeTransportEvent(NetworkEvent.Data, senderId, channel, payload);
                }
            }
        }

        // -------------- Utility Methods -----------------------------------------------------------------------------

        /// <summary>
        /// Invoke Transport Events.
        /// </summary>
        /// <param name="networkEvent">Network Event Type</param>
        /// <param name="senderId">Peer Sender ID</param>
        /// <param name="channel">Communication Channel</param>
        /// <param name="payload">Event Payload</param>
        private void InvokeTransportEvent(NetworkEvent networkEvent, ulong senderId = 0, NetworkChannel channel = default, ArraySegment<byte> payload = default)
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
                    InvokeOnTransportEvent(networkEvent, senderId, channel, payload, Time.realtimeSinceStartup);
                    break;
            }
        }

        /// <summary>
        /// Convert the <see cref="MLAPI.Transports.ChannelType"/> to <see cref="ExitGames.Client.Photon.SendOptions"/>
        /// </summary>
        /// <param name="deliveryMode">Channel Type to convert</param>
        /// <returns>SendOption type based on the ChannelType</returns>
        private SendOptions MlapiChannelTypeToSendOptions(NetworkDelivery deliveryMode)
        {
            switch (deliveryMode)
            {
                case NetworkDelivery.Unreliable:
                    return SendOptions.SendUnreliable;
                default:
                    return SendOptions.SendReliable;
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
        private void DeInit()
        {
            m_IdToChannel.Clear();
            m_ChannelToId.Clear();
            m_Channels.Clear();
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

            if (NetworkManager.Singleton.IsHost)
            {
                NetworkManager.Singleton.StopHost();
            }
            else if (NetworkManager.Singleton.IsClient)
            {
                NetworkManager.Singleton.StopClient();
            }
            else if (NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.StopServer();
            }
        }

        // -------------- Utility Types -------------------------------------------------------------------------------

        /// <summary>
        /// Memory Stream controller to store several events into one single buffer
        /// </summary>
        private class SendQueue
        {
            MemoryStream m_Stream;

            /// <summary>
            /// The size of the send queue.
            /// </summary>
            public int Size { get; }

            public SendQueue(int size)
            {
                Size = size;
                byte[] buffer = new byte[size];
                m_Stream = new MemoryStream(buffer, 0, buffer.Length, true, true);
            }

            /// <summary>
            /// Ads an event to the send queue.
            /// </summary>
            /// <param name="channelId">The channel this event should be sent on.</param>
            /// <param name="data">The data to send.</param>
            /// <returns>True if the event was added successfully to the queue. False if there was no space in the queue.</returns>
            internal bool AddEvent(byte channelId, ArraySegment<byte> data)
            {
                if (m_Stream.Position + data.Count + 4 > Size)
                {
                    return false;
                }

                using (PooledNetworkWriter writer = PooledNetworkWriter.Get(m_Stream))
                {
                    writer.WriteByte(channelId);
                    writer.WriteInt32Packed(data.Count);
                    Array.Copy(data.Array, data.Offset, m_Stream.GetBuffer(), m_Stream.Position, data.Count);
                    m_Stream.Position += data.Count;
                }

                return true;
            }

            internal void Clear()
            {
                m_Stream.Position = 0;
            }

            internal bool IsEmpty()
            {
                return m_Stream.Position == 0;
            }

            internal ArraySegment<byte> GetData()
            {
                return new ArraySegment<byte>(m_Stream.GetBuffer(), 0, (int)m_Stream.Position);
            }
        }

        /// <summary>
        /// Communication Channel information
        /// </summary>
        private struct RealtimeChannel
        {
            public byte Id;
            public SendOptions SendMode;
        }

        /// <summary>
        /// Cached information about reliability mode with a certain client
        /// </summary>
        private struct SendTarget
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
        private enum BatchMode : byte
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
