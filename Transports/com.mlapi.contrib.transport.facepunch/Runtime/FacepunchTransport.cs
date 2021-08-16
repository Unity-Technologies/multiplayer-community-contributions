using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MLAPI.Logging;
using MLAPI.Transports.Tasks;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace MLAPI.Transports.Facepunch
{
    using SocketConnection = Steamworks.Data.Connection;

    public class FacepunchTransport : NetworkTransport, IConnectionManager, ISocketManager
    {
        private ConnectionManager connectionManager;
        private SocketManager socketManager;
        private Dictionary<ulong, Client> connectedClients;
        private Dictionary<NetworkChannel, SendType> channelSendTypes;

        [SerializeField] private List<TransportChannel> channels = new List<TransportChannel>();

        [Space]
        [Tooltip("The Steam App ID of your game. Technically you're not allowed to use 480, but Valve doesn't do anything about it so it's fine for testing purposes.")]
        [SerializeField] private uint steamAppId = 480;

        [Tooltip("The Steam ID of the user targeted when joining as a client.")]
        [SerializeField] public ulong targetSteamId;

        [Header("Info")]
        [ReadOnly]
        [Tooltip("When in play mode, this will display your Steam ID.")]
        [SerializeField] private ulong userSteamId;

        private LogLevel LogLevel => NetworkManager.Singleton.LogLevel;

        private class Client
        {
            public SteamId steamId;
            public SocketConnection connection;
        }

        #region MonoBehaviour Messages

        private void Awake()
        {
            try
            {
                SteamClient.Init(steamAppId, false);
            }
            catch (Exception e)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{nameof(FacepunchTransport)}] - Caught an exeption during initialization of Steam client: {e}");
            }
            finally
            {
                StartCoroutine(InitSteamworks());
            }
        }

        private void Update()
        {
            SteamClient.RunCallbacks();
        }

        private void OnDestroy()
        {
            SteamClient.Shutdown();
        }

        #endregion

        #region NetworkTransport Overrides

        public override ulong ServerClientId => 0;

        public override void DisconnectLocalClient()
        {
            connectionManager.Connection.Close();

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Disconnecting local client.");
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (connectedClients.TryGetValue(clientId, out Client user))
            {
                user.connection.Close();
                connectedClients.Remove(clientId);

                if (LogLevel <= LogLevel.Developer)
                    Debug.Log($"[{nameof(FacepunchTransport)}] - Disconnecting remote client with ID {clientId}.");
            }
            else if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning($"[{nameof(FacepunchTransport)}] - Failed to disconnect remote client with ID {clientId}, client not connected.");
        }

        public override unsafe ulong GetCurrentRtt(ulong clientId)
        {
            return 0;
        }

        public override void Init()
        {
            connectedClients = new Dictionary<ulong, Client>();
            channelSendTypes = new Dictionary<NetworkChannel, SendType>();

            foreach (TransportChannel channel in MLAPI_CHANNELS.Concat(channels))
            {
                SendType sendType = channel.Delivery switch
                {
                    NetworkDelivery.Reliable => SendType.Reliable,
                    NetworkDelivery.ReliableFragmentedSequenced => SendType.Reliable,
                    NetworkDelivery.ReliableSequenced => SendType.Reliable,
                    NetworkDelivery.Unreliable => SendType.Unreliable,
                    NetworkDelivery.UnreliableSequenced => SendType.Unreliable,
                    _ => SendType.Reliable
                };

                channelSendTypes.Add(channel.Channel, sendType);
            }
        }

        public override void Shutdown()
        {
            try
            {
                if (LogLevel <= LogLevel.Developer)
                    Debug.Log($"[{nameof(FacepunchTransport)}] - Shutting down.");

                connectionManager?.Close();
                socketManager?.Close();
            }
            catch (Exception e)
            {
                if (LogLevel <= LogLevel.Error)
                    Debug.LogError($"[{nameof(FacepunchTransport)}] - Caught an exception while shutting down: {e}");
            }
        }

        public override unsafe void Send(ulong clientId, ArraySegment<byte> data, NetworkChannel networkChannel)
        {
            if (!channelSendTypes.TryGetValue(networkChannel, out SendType sendType))
                if (!channelSendTypes.TryGetValue(NetworkChannel.DefaultMessage, out sendType))
                    sendType = SendType.Reliable;

            byte* buffer = stackalloc byte[data.Count + 1];
            fixed (byte* pointer = data.Array)
                Buffer.MemoryCopy(pointer + data.Offset, buffer, data.Count, data.Count);
            buffer[data.Count] = (byte)networkChannel;

            if (clientId == ServerClientId)
                connectionManager.Connection.SendMessage((IntPtr)buffer, data.Count + 1, sendType);
            else if (connectedClients.TryGetValue(clientId, out Client user))
                user.connection.SendMessage((IntPtr)buffer, data.Count + 1, sendType);
            else if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning($"[{nameof(FacepunchTransport)}] - Failed to send packet to remote client with ID {clientId}, client not connected.");
        }

        public override NetworkEvent PollEvent(out ulong clientId, out NetworkChannel networkChannel, out ArraySegment<byte> payload, out float receiveTime)
        {
            connectionManager?.Receive();
            socketManager?.Receive();

            clientId = 0;
            networkChannel = default;
            receiveTime = Time.realtimeSinceStartup;
            return NetworkEvent.Nothing;
        }

        public override SocketTasks StartClient()
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Starting as client.");

            connectionManager = SteamNetworkingSockets.ConnectRelay<ConnectionManager>(targetSteamId);
            connectionManager.Interface = this;
            return SocketTask.Working.AsTasks();
        }

        public override SocketTasks StartServer()
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Starting as server.");

            socketManager = SteamNetworkingSockets.CreateRelaySocket<SocketManager>();
            socketManager.Interface = this;
            return SocketTask.Done.AsTasks();
        }

        #endregion

        #region ConnectionManager Implementation

        void IConnectionManager.OnConnecting(ConnectionInfo info)
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Connecting with Steam user {info.Identity.SteamId}.");
        }

        void IConnectionManager.OnConnected(ConnectionInfo info)
        {
            InvokeOnTransportEvent(NetworkEvent.Connect, ServerClientId, NetworkChannel.ChannelUnused, default, Time.realtimeSinceStartup);

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Connected with Steam user {info.Identity.SteamId}.");
        }

        void IConnectionManager.OnDisconnected(ConnectionInfo info)
        {
            InvokeOnTransportEvent(NetworkEvent.Disconnect, ServerClientId, NetworkChannel.ChannelUnused, default, Time.realtimeSinceStartup);

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Disconnected Steam user {info.Identity.SteamId}.");
        }

        void IConnectionManager.OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            byte[] payload = new byte[size];
            Marshal.Copy(data, payload, 0, size);
            InvokeOnTransportEvent(NetworkEvent.Data, ServerClientId, (NetworkChannel)payload[size - 1], new ArraySegment<byte>(payload, 0, size - 1), Time.realtimeSinceStartup);
        }

        #endregion

        #region SocketManager Implementation

        void ISocketManager.OnConnecting(SocketConnection connection, ConnectionInfo info)
        {
            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Accepting connection from Steam user {info.Identity.SteamId}.");

            connection.Accept();
        }

        void ISocketManager.OnConnected(SocketConnection connection, ConnectionInfo info)
        {
            if (!connectedClients.ContainsKey(connection.Id))
            {
                connectedClients.Add(connection.Id, new Client()
                {
                    connection = connection,
                    steamId = info.Identity.SteamId
                });

                InvokeOnTransportEvent(NetworkEvent.Connect, connection.Id, NetworkChannel.ChannelUnused, default, Time.realtimeSinceStartup);

                if (LogLevel <= LogLevel.Developer)
                    Debug.Log($"[{nameof(FacepunchTransport)}] - Connected with Steam user {info.Identity.SteamId}.");
            }
            else if (LogLevel <= LogLevel.Normal)
                Debug.LogWarning($"[{nameof(FacepunchTransport)}] - Failed to connect client with ID {connection.Id}, client already connected.");
        }

        void ISocketManager.OnDisconnected(SocketConnection connection, ConnectionInfo info)
        {
            connectedClients.Remove(connection.Id);

            InvokeOnTransportEvent(NetworkEvent.Disconnect, connection.Id, NetworkChannel.ChannelUnused, default, Time.realtimeSinceStartup);

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Disconnected Steam user {info.Identity.SteamId}");
        }

        void ISocketManager.OnMessage(SocketConnection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel)
        {
            byte[] payload = new byte[size];
            Marshal.Copy(data, payload, 0, size);
            InvokeOnTransportEvent(NetworkEvent.Data, connection.Id, (NetworkChannel)payload[size - 1], new ArraySegment<byte>(payload, 0, size - 1), Time.realtimeSinceStartup);
        }

        #endregion

        #region Utility Methods

        private IEnumerator InitSteamworks()
        {
            yield return new WaitUntil(() => SteamClient.IsValid);

            SteamNetworkingUtils.InitRelayNetworkAccess();

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Initialized access to Steam Relay Network.");

            userSteamId = SteamClient.SteamId;

            if (LogLevel <= LogLevel.Developer)
                Debug.Log($"[{nameof(FacepunchTransport)}] - Fetched user Steam ID.");
        }

        #endregion
    }
}