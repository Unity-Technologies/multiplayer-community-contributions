#if !DISABLESTEAMWORKS && STEAMWORKSNET && NETCODEGAMEOBJECTS
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;
using System;
using Unity.Netcode;
using Debug = UnityEngine.Debug;
using System.Runtime.InteropServices;

namespace Netcode.Transports
{
    public class SteamNetworkingSocketsTransport : NetworkTransport
    {
        #region Internal Object Model
        private class SteamConnectionData
        {
            internal SteamConnectionData(CSteamID steamId)
            {
                id = steamId;
            }

            internal CSteamID id;
            internal HSteamNetConnection connection;
        }

        private Callback<SteamNetConnectionStatusChangedCallback_t> c_onConnectionChange = null;
        private HSteamListenSocket listenSocket;
        private SteamConnectionData serverUser;
        private readonly Dictionary<ulong, SteamConnectionData> connectionMapping = new Dictionary<ulong, SteamConnectionData>();
        private readonly Queue<SteamNetConnectionStatusChangedCallback_t> connectionStatusChangeQueue = new Queue<SteamNetConnectionStatusChangedCallback_t>();
        private bool isServer = false;
        #endregion

        public ulong ConnectToSteamID;
        public SteamNetworkingConfigValue_t[] options = new SteamNetworkingConfigValue_t[0];

        public override ulong ServerClientId => 0;
        public override bool IsSupported
        {
            get
            {
                try
                {
#if UNITY_SERVER
                    InteropHelp.TestIfAvailableGameServer();
#else
                    InteropHelp.TestIfAvailableClient();
#endif
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        public override void DisconnectLocalClient()
        {
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer) NetworkLog.LogInfoServer(nameof(SteamNetworkingSocketsTransport) + " - DisconnectLocalClient");

            if (connectionMapping.ContainsKey(serverUser.id.m_SteamID))
                connectionMapping.Remove(serverUser.id.m_SteamID);
#if UNITY_SERVER
            SteamGameServerNetworkingSockets.CloseConnection(serverUser.connection, 0, "Disconnected", false);
#else
            SteamNetworkingSockets.CloseConnection(serverUser.connection, 0, "Disconnected", false);
#endif
            serverUser = null;
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer) 
                NetworkLog.LogInfoServer(nameof(SteamNetworkingSocketsTransport.DisconnectRemoteClient) + " - clientId: " + clientId);

            if (!connectionMapping.ContainsKey(clientId))
            {
                if (NetworkManager.Singleton.LogLevel <= LogLevel.Error) 
                    NetworkLog.LogErrorServer(nameof(SteamNetworkingSocketsTransport) + " - Can't disconect client, client not connected, clientId: " + clientId);
                return;
            }

#if UNITY_SERVER
            SteamGameServerNetworkingSockets.CloseConnection(connectionMapping[clientId].connection, 0, "Disconnected", false);
#else
            SteamNetworkingSockets.CloseConnection(connectionMapping[clientId].connection, 0, "Disconnected", false);
#endif

            connectionMapping.Remove(clientId);
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            if (isServer)
            {
                if (connectionMapping.ContainsKey(clientId))
                {
                    //TODO: We need to figure out how Valve expects you to use ISteamNetworkingUtils ... the issue is no one thought to document WTF a SteamNetworkingPingLocation was or how to get them
                    return 0ul;
                }
                else
                {
                    if (NetworkManager.Singleton.LogLevel <= LogLevel.Error)
                        NetworkLog.LogErrorServer(nameof(SteamNetworkingSocketsTransport) + " - Can't GetCurrentRtt from client, client not connected, clientId: " + clientId);
                }
            }
            else
            {
                return 0ul;
            }

            return 0ul;
        }

        public override void Initialize(NetworkManager networkManager = null)
        {
            if (!IsSupported)
            {
                if (NetworkManager.Singleton.LogLevel <= LogLevel.Error)
                    NetworkLog.LogErrorServer(nameof(SteamNetworkingSocketsTransport) + " - Initialize - Steamworks.NET not ready, " + nameof(SteamNetworkingSocketsTransport) + " can not run without it");
                return;
            }
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            //Handle any connection state changes we may have
            #region Connnection State Changes
            while (connectionStatusChangeQueue.Count > 0)
            {
                var param = connectionStatusChangeQueue.Dequeue();

                if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
                {
                    //This happens when someone asked to connect to us, in the case of NetCode for GameObject this should only happen if we are a server/host
                    //the current standard is to blindly accept ... NetCode for GO should really consider a validation model for connections
                    if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                        NetworkLog.LogInfoServer(nameof(SteamNetworkingSocketsTransport) + " - connection request from " + param.m_info.m_identityRemote.GetSteamID64());

                    if (isServer)
                    {
                        EResult res;
#if UNITY_SERVER
                        if ((res = SteamGameServerNetworkingSockets.AcceptConnection(param.m_hConn)) == EResult.k_EResultOK)
#else
                        if ((res = SteamNetworkingSockets.AcceptConnection(param.m_hConn)) == EResult.k_EResultOK)
#endif
                        {
                            if (isServer)
                            {
                                if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                                    Debug.Log($"Accepting connection {param.m_info.m_identityRemote.GetSteamID64()}");


                                clientId = param.m_info.m_identityRemote.GetSteamID64();
                                payload = new ArraySegment<byte>();
                                receiveTime = Time.realtimeSinceStartup;

                                //This should be a new connection, record it
                                if (connectionMapping.ContainsKey(clientId) == false)
                                {
                                    var nCon = new SteamConnectionData(param.m_info.m_identityRemote.GetSteamID());
                                    nCon.connection = param.m_hConn;
                                    connectionMapping.Add(clientId, nCon);
                                }
                            }
                            else
                            {
                                if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                                    Debug.Log($"Connection {param.m_info.m_identityRemote.GetSteamID64()} could not be accepted: this is not a server");
                            }
                        }
                        else
                        {
                            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                                Debug.Log($"Connection {param.m_info.m_identityRemote.GetSteamID64()} could not be accepted: {res}");
                        }
                    }
                }
                else if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
                {
                    if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                        NetworkLog.LogInfoServer(nameof(SteamNetworkingSocketsTransport) + " - connection request to " + param.m_info.m_identityRemote.GetSteamID64() + " was accepted!");

                    clientId = param.m_info.m_identityRemote.GetSteamID64();
                    payload = new ArraySegment<byte>();
                    receiveTime = Time.realtimeSinceStartup;

                    //We should already have it but if not record the server connection that was just accepted
                    if (connectionMapping.ContainsKey(clientId) == false)
                    {
                        var nCon = new SteamConnectionData(param.m_info.m_identityRemote.GetSteamID());
                        nCon.connection = param.m_hConn;
                        connectionMapping.Add(clientId, nCon);
                    }
                    else
                    {
                        //Should be redundent but update the conneciton handle anyway
                        connectionMapping[clientId].connection = param.m_hConn;
                    }

                    return NetworkEvent.Connect;
                }
                else if (param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer 
                    || param.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
                {
                    //The connection is no more
                    if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                        NetworkLog.LogInfoServer(nameof(SteamNetworkingSocketsTransport) + $" - connection closed for {param.m_info.m_identityRemote.GetSteamID64()} state responce: {param.m_info.m_eState}");

                    clientId = param.m_info.m_identityRemote.GetSteamID64();
                    payload = new ArraySegment<byte>();
                    receiveTime = Time.realtimeSinceStartup;

                    //Remove the mapped connection info
                    if (connectionMapping.ContainsKey(clientId) != false)
                        connectionMapping.Remove(clientId);

                    return NetworkEvent.Disconnect;
                }
                else
                {
                    if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                        Debug.Log($"Connection {param.m_info.m_identityRemote.GetSteamID64()} state changed: {param.m_info.m_eState}");
                }
            }
            #endregion

            foreach (var connectionData in connectionMapping.Values)
            {
                IntPtr[] ptrs = new IntPtr[1];
                int messageCount;

#if UNITY_SERVER
                if ((messageCount = SteamGameServerNetworkingSockets.ReceiveMessagesOnConnection(connectionData.connection, ptrs, 1)) > 0)
#else
                if ((messageCount = SteamNetworkingSockets.ReceiveMessagesOnConnection(connectionData.connection, ptrs, 1)) > 0)
#endif
                {
                    if (messageCount > 0)
                    {
                        clientId = connectionData.id.m_SteamID;

                        SteamNetworkingMessage_t data = Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptrs[0]);
                        var buffer = new byte[data.m_cbSize];
                        Marshal.Copy(data.m_pData, buffer, 0, data.m_cbSize);
                        payload = buffer;
                        SteamNetworkingMessage_t.Release(ptrs[0]);
                        
                        receiveTime = Time.realtimeSinceStartup;
                        return NetworkEvent.Data;
                    }
                }
            }

            payload = new ArraySegment<byte>();
            clientId = 0;
            receiveTime = Time.realtimeSinceStartup;
            return NetworkEvent.Nothing;
        }

        public override void Send(ulong clientId, ArraySegment<byte> segment, NetworkDelivery delivery)
        {
            if(clientId == 0)
                clientId = serverUser.id.m_SteamID;

            //Check if we have a mapped user for this ID
            if (connectionMapping.ContainsKey(clientId))
            {
                //Grab a pointer to the user's connection
                var connection = connectionMapping[clientId].connection;

                //Build a standard array + 1 for the channel
                byte[] data = new byte[segment.Count + 1];
                Array.Copy(segment.Array, segment.Offset, data, 0, segment.Count);
                //The segment count is the last index in our (+1) array, on that last index write a byte indicating the delivery channel
                data[segment.Count] = Convert.ToByte((int)delivery);
                //Create an unmanaged array and get a pointer to it
                GCHandle pinnedArray = GCHandle.Alloc(data, GCHandleType.Pinned);
                IntPtr pData = pinnedArray.AddrOfPinnedObject();
                //Translate the NetworkDelivery (channle) to the Steam QoS send type .. we assume unreliable if nothing matches
                int sendFlag = Constants.k_nSteamNetworkingSend_Unreliable;
                switch (delivery)
                {
                    case NetworkDelivery.Reliable:
                    case NetworkDelivery.ReliableFragmentedSequenced:
                        sendFlag = Constants.k_nSteamNetworkingSend_Reliable;
                        break;
                    case NetworkDelivery.ReliableSequenced:
                        sendFlag = Constants.k_nSteamNetworkingSend_ReliableNoNagle;
                        break;
                    case NetworkDelivery.UnreliableSequenced:
                        sendFlag = Constants.k_nSteamNetworkingSend_UnreliableNoNagle;
                        break;
                }
                //Send to the target
#if UNITY_SERVER
                EResult responce = SteamGameServerNetworkingSockets.SendMessageToConnection(connection, pData, (uint)data.Length, sendFlag, out long _);
#else
                EResult responce = SteamNetworkingSockets.SendMessageToConnection(connection, pData, (uint)data.Length, sendFlag, out long _);
#endif
                //Free the unmanaged array
                pinnedArray.Free();

                //If we had some error report that and move on
                if ((responce == EResult.k_EResultNoConnection || responce == EResult.k_EResultInvalidParam)
                    && NetworkManager.Singleton.LogLevel <= LogLevel.Normal)
                {
                    Debug.LogWarning($"Connection to server was lost.");
#if UNITY_SERVER
                    SteamGameServerNetworkingSockets.CloseConnection(connection, 0, "Disconnected", false);
#else
                    SteamNetworkingSockets.CloseConnection(connection, 0, "Disconnected", false);
#endif
                }
                else if (responce != EResult.k_EResultOK 
                    && NetworkManager.Singleton.LogLevel <= LogLevel.Error)
                {
                    Debug.LogError($"Could not send: {responce}");
                }
            }
            else
            {
                //No client found so report that
                if (NetworkManager.Singleton.LogLevel <= LogLevel.Error)
                {
                    Debug.LogError("Trying to send on unknown connection: " + clientId);
                    NetworkLog.LogErrorServer(nameof(SteamNetworkingSocketsTransport.Send) + " - Trying to send on unknown connection: " + clientId);
                }
            }
        }

        public override void Shutdown()
        {
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                UnityEngine.Debug.Log(nameof(SteamNetworkingSocketsTransport.Shutdown));

            if (isServer)
            {
#if UNITY_SERVER
                SteamGameServerNetworkingSockets.CloseListenSocket(listenSocket);
#else
                SteamNetworkingSockets.CloseListenSocket(listenSocket);
#endif
            }
            else
            {
                if (serverUser != null)
                {
                    if (connectionMapping.ContainsKey(serverUser.id.m_SteamID))
                        connectionMapping.Remove(serverUser.id.m_SteamID);
#if UNITY_SERVER
                    SteamGameServerNetworkingSockets.CloseConnection(serverUser.connection, 0, "Disconnected", false);
#else
                    SteamNetworkingSockets.CloseConnection(serverUser.connection, 0, "Disconnected", false);
#endif
                }
            }

            isServer = false;

            if (NetworkManager.Singleton != null)
            {
                //Delay
                NetworkManager.Singleton.StartCoroutine(Delay(0.1f, () =>
                {
                    CloseP2PSessions();
                }));
            }
            else
            {
                CloseP2PSessions();
            }
        }

        public override bool StartClient()
        {
            if (c_onConnectionChange == null)
                c_onConnectionChange = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

            serverUser = new SteamConnectionData(new CSteamID(ConnectToSteamID));

            try
            {
#if UNITY_SERVER
                SteamGameServerNetworkingUtils.InitRelayNetworkAccess();
#else
                SteamNetworkingUtils.InitRelayNetworkAccess();
#endif
                SteamNetworkingIdentity smi = new SteamNetworkingIdentity();
                smi.SetSteamID(serverUser.id);
#if UNITY_SERVER
                serverUser.connection = SteamGameServerNetworkingSockets.ConnectP2P(ref smi, 0, options.Length, options);
#else
                serverUser.connection = SteamNetworkingSockets.ConnectP2P(ref smi, 0, options.Length, options);
#endif
                connectionMapping.Add(ConnectToSteamID, serverUser);

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("Exception: " + ex.Message + ". Client could not be started.");
                return false;
            }
        }

        public override bool StartServer()
        {
            isServer = true;

            if(c_onConnectionChange == null)
                c_onConnectionChange = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnConnectionStatusChanged);

            if (options == null)
                options = new SteamNetworkingConfigValue_t[0];

#if UNITY_SERVER
            listenSocket = SteamGameServerNetworkingSockets.CreateListenSocketP2P(0, options.Length, options);
#else
            listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, options.Length, options);
#endif

            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                UnityEngine.Debug.Log(nameof(SteamNetworkingSocketsTransport.StartServer));

            return true;
        }

        private void CloseP2PSessions()
        {
            if (serverUser != null)
            {
                if(connectionMapping.ContainsKey(serverUser.id.m_SteamID))
                    connectionMapping.Remove(serverUser.id.m_SteamID);
#if UNITY_SERVER
                SteamGameServerNetworkingSockets.CloseConnection(serverUser.connection, 0, "Disconnected", false);
#else
                SteamNetworkingSockets.CloseConnection(serverUser.connection, 0, "Disconnected", false);
#endif
            }

            foreach (SteamConnectionData user in connectionMapping.Values)
            {
#if UNITY_SERVER
                SteamGameServerNetworkingSockets.CloseConnection(user.connection, 0, "Disconnected", false);
#else
                SteamNetworkingSockets.CloseConnection(user.connection, 0, "Disconnected", false);
#endif
            }

            connectionMapping.Clear();
            serverUser = null;
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                UnityEngine.Debug.Log(nameof(SteamNetworkingSocketsTransport) + " - CloseP2PSessions - has Closed P2P Sessions With all Users");

            if (c_onConnectionChange != null)
            {
                c_onConnectionChange.Dispose();
                c_onConnectionChange = null;
            }
        }

        private void OnConnectionStatusChanged(SteamNetConnectionStatusChangedCallback_t param)
        {
            connectionStatusChangeQueue.Enqueue(param);
        }

        private static IEnumerator Delay(float time, Action action)
        {
            yield return new WaitForSeconds(time);
            action.Invoke();
        }
    }
}
#endif