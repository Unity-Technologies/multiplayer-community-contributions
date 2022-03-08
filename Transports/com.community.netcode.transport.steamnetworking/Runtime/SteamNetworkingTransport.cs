#if !DISABLESTEAMWORKS
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Unity.Netcode;

/*
 * Steamworks API Reference for ISteamNetworking: https://partner.steamgames.com/doc/api/ISteamNetworking
 * Steamworks.NET: https://steamworks.github.io/
 */

namespace Netcode.Transports
{
    public class SteamNetworkingTransport : NetworkTransport
    {
        private Callback<P2PSessionRequest_t> _p2PSessionRequestCallback;
        private Callback<P2PSessionConnectFail_t> _p2PSessionConnectFailCallback;

        public ulong ConnectToSteamID;

        private class User
        {
            public User(CSteamID steamId)
            {
                SteamId = steamId;
            }

            public CSteamID SteamId;
            public Ping Ping = new Ping();
        }

        private User serverUser;
        private Dictionary<ulong, User> connectedUsers = new Dictionary<ulong, User>();
        private bool isServer = false;

        //holds information for a failed connection attempt to use in poll function to forward the event.
        private bool connectionAttemptFailed = false;
        private ulong connectionAttemptFailedClientId;

        private enum InternalChannelType
        {
            Connect = 0,
            Disconnect = 1,
            Ping = 2,
            Pong = 3,
            NetcodeData = 4,
            InternalChannelsCount = 5,
        }

        private class Ping
        {
            private List<uint> lastPings = new List<uint>();
            private List<uint> sortedPings = new List<uint>();
            private uint pingValue = 0;

            public void SetPing(uint ping)
            {
                lastPings.Add(ping);
                sortedPings.Add(ping);

                if (lastPings.Count > 10)
                {
                    sortedPings.Remove(lastPings[0]);
                    lastPings.RemoveAt(0);
                }

                sortedPings.Sort();

                pingValue = sortedPings[Mathf.FloorToInt(lastPings.Count / 2)];
            }

            public uint Get()
            {
                return pingValue;
            }
        }

        private class PingTracker
        {
            Stopwatch stopwatch = new Stopwatch();

            public PingTracker()
            {
                stopwatch.Start();
            }

            public uint getPingTime()
            {
                return (uint)stopwatch.ElapsedMilliseconds;
            }
        }

        private Dictionary<byte, PingTracker> sentPings = new Dictionary<byte, PingTracker>(128);
        private byte pingIdCounter = 0;
        public readonly double PingInterval = 0.25;
        private bool sendPings = false;

        byte[] messageBuffer = new byte[1200];
        byte[] pingPongMessageBuffer = new byte[1];

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
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer) NetworkLog.LogInfoServer(nameof(SteamNetworkingTransport) + " - DisconnectLocalClient");

#if UNITY_SERVER
            SteamGameServerNetworking.SendP2PPacket(serverUser.SteamId, new byte[] { 0 }, 1, EP2PSend.k_EP2PSendReliable, (int)InternalChannelType.Disconnect);
#else
            SteamNetworking.SendP2PPacket(serverUser.SteamId, new byte[] { 0 }, 1, EP2PSend.k_EP2PSendReliable, (int)InternalChannelType.Disconnect);
#endif
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer) NetworkLog.LogInfoServer(nameof(SteamNetworkingTransport) + " - DisconnectRemoteClient clientId: " + clientId);

            if (!connectedUsers.ContainsKey(clientId))
            {
                if (NetworkManager.Singleton.LogLevel <= LogLevel.Error) NetworkLog.LogErrorServer(nameof(SteamNetworkingTransport) + " - Can't disconect client, client not connected, clientId: " + clientId);
                return;
            }

#if UNITY_SERVER
            SteamGameServerNetworking.SendP2PPacket(connectedUsers[clientId].SteamId, new byte[] { 0 }, 1, EP2PSend.k_EP2PSendReliable, (int)InternalChannelType.Disconnect);
#else
            SteamNetworking.SendP2PPacket(connectedUsers[clientId].SteamId, new byte[] { 0 }, 1, EP2PSend.k_EP2PSendReliable, (int)InternalChannelType.Disconnect);
#endif
            CSteamID steamId = connectedUsers[clientId].SteamId;

            NetworkManager.Singleton.StartCoroutine(Delay(100, () =>
            {
                //Need to delay the closing of the p2p sessions to not block the disconect message before it is sent.
#if UNITY_SERVER
                SteamGameServerNetworking.CloseP2PSessionWithUser(steamId);
#else
                SteamNetworking.CloseP2PSessionWithUser(steamId);
#endif
                if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                    NetworkLog.LogInfoServer(nameof(SteamNetworkingTransport) + " - DisconnectRemoteClient - has Closed P2P Session With clientId: " + clientId);
            }));

            connectedUsers.Remove(clientId);
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            if (isServer)
            {
                if (clientId == ServerClientId)
                {
                    return 0;
                }

                if (connectedUsers.ContainsKey(clientId))
                {
                    return connectedUsers[clientId].Ping.Get();
                }
                else
                {
                    if (NetworkManager.Singleton.LogLevel <= LogLevel.Error)
                        NetworkLog.LogErrorServer(nameof(SteamNetworkingTransport) + " - Can't GetCurrentRtt from client, client not connected, clientId: " + clientId);
                }
            }
            else
            {
                return serverUser.Ping.Get();
            }

            return 0ul;
        }

        public override void Initialize(NetworkManager networkManager = null)
        {
            if (!IsSupported)
            {
                if (NetworkManager.Singleton.LogLevel <= LogLevel.Error)
                    NetworkLog.LogErrorServer(nameof(SteamNetworkingTransport) + " - Initialize - Steamworks.NET not ready, " + nameof(SteamNetworkingTransport) + " can not run without it");
                return;
            }
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            //Connect fail disconnect
            if (connectionAttemptFailed)
            {
                clientId = connectionAttemptFailedClientId;
                payload = new ArraySegment<byte>();
                connectionAttemptFailed = false;
                receiveTime = Time.realtimeSinceStartup;
                return NetworkEvent.Disconnect;
            }

            var currentPollChannel = 0;
            while (currentPollChannel < (int)InternalChannelType.InternalChannelsCount)
            {
#if UNITY_SERVER
                if (SteamGameServerNetworking.IsP2PPacketAvailable(out uint msgSize, currentPollChannel))
#else

                if (SteamNetworking.IsP2PPacketAvailable(out uint msgSize, currentPollChannel))
#endif
                {
                    uint bytesRead;
                    CSteamID remoteId;
                    if (messageBuffer.Length < msgSize)
                    {
                        messageBuffer = new byte[msgSize];
                        if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                            NetworkLog.LogInfoServer(nameof(SteamNetworkingTransport) + " - PollEvent - Increase buffer size to: " + msgSize);
                    }

#if UNITY_SERVER
                    if (SteamGameServerNetworking.ReadP2PPacket(messageBuffer, msgSize, out bytesRead, out remoteId, currentPollChannel))
#else

                    if (SteamNetworking.ReadP2PPacket(messageBuffer, msgSize, out bytesRead, out remoteId, currentPollChannel))
#endif
                    {
                        clientId = remoteId.m_SteamID;

                        payload = new ArraySegment<byte>();

                        switch (currentPollChannel)
                        {
                            case (byte)InternalChannelType.Disconnect:

                                connectedUsers.Remove(clientId);
#if UNITY_SERVER
                                    SteamGameServerNetworking.CloseP2PSessionWithUser(remoteId);
#else
                                SteamNetworking.CloseP2PSessionWithUser(remoteId);
#endif
                                receiveTime = Time.realtimeSinceStartup;
                                return NetworkEvent.Disconnect;

                            case (byte)InternalChannelType.Connect:

                                if (isServer)
                                {
#if UNITY_SERVER
                                        SteamGameServerNetworking.SendP2PPacket(remoteId, new byte[] { 0 }, 1, EP2PSend.k_EP2PSendReliable, (int)InternalChannelType.Connect);
#else
                                    SteamNetworking.SendP2PPacket(remoteId, new byte[] { 0 }, 1, EP2PSend.k_EP2PSendReliable, (int)InternalChannelType.Connect);
#endif
                                }

                                if (connectedUsers.ContainsKey(remoteId.m_SteamID) == false)
                                {
                                    clientId = remoteId.m_SteamID;
                                    connectedUsers.Add(clientId, new User(remoteId));
                                    receiveTime = Time.realtimeSinceStartup;

                                    if (!isServer)
                                    {
                                        OnConnected();
                                    }

                                    return NetworkEvent.Connect;
                                }

                                break;

                            case (byte)InternalChannelType.Ping:

                                pingPongMessageBuffer[0] = messageBuffer[0];
#if UNITY_SERVER
                                    SteamGameServerNetworking.SendP2PPacket(remoteId, pingPongMessageBuffer, msgSize, EP2PSend.k_EP2PSendUnreliableNoDelay, (int)InternalChannelType.Pong);
#else
                                SteamNetworking.SendP2PPacket(remoteId, pingPongMessageBuffer, msgSize, EP2PSend.k_EP2PSendUnreliableNoDelay, (int)InternalChannelType.Pong);
#endif
                                receiveTime = Time.realtimeSinceStartup;
                                break;

                            case (byte)InternalChannelType.Pong:

                                uint pingValue = sentPings[messageBuffer[0]].getPingTime();
                                if (isServer)
                                {
                                    connectedUsers[remoteId.m_SteamID].Ping.SetPing(pingValue);
                                }
                                else
                                {
                                    serverUser.Ping.SetPing(pingValue);
                                }

                                receiveTime = Time.realtimeSinceStartup;
                                break;
                            
                            case (byte)InternalChannelType.NetcodeData:
                                payload = new ArraySegment<byte>(messageBuffer, 0, (int)msgSize);
                                receiveTime = Time.realtimeSinceStartup;
                                return NetworkEvent.Data;
                            default:
                                throw new InvalidOperationException();
                        }
                    }
                    else
                    {
                        currentPollChannel++;
                    }
                }
                else
                {
                    currentPollChannel++;
                }
            }

            currentPollChannel = 0;
            payload = new ArraySegment<byte>();
            clientId = 0;
            receiveTime = Time.realtimeSinceStartup;
            return NetworkEvent.Nothing;
        }

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            EP2PSend sendType = NetworkDeliveryToEP2PSend(delivery);

            if (clientId == ServerClientId)
            {
#if UNITY_SERVER
                SteamGameServerNetworking.SendP2PPacket(serverUser.SteamId, data.Array, (uint)data.Count, sendType, (int)InternalChannelType.NetcodeData);
#else
                SteamNetworking.SendP2PPacket(serverUser.SteamId, data.Array, (uint)data.Count, sendType, (int)InternalChannelType.NetcodeData);
#endif
            }
            else
            {
                if (connectedUsers.ContainsKey(clientId))
                {
#if UNITY_SERVER
                    SteamGameServerNetworking.SendP2PPacket(connectedUsers[clientId].SteamId, data.Array, (uint)data.Count, sendType, (int)InternalChannelType.NetcodeData);
#else
                    SteamNetworking.SendP2PPacket(connectedUsers[clientId].SteamId, data.Array, (uint)data.Count, sendType, (int)InternalChannelType.NetcodeData);
#endif
                }
                else
                {
                    if (NetworkManager.Singleton.LogLevel <= LogLevel.Error)
                        NetworkLog.LogErrorServer(nameof(SteamNetworkingTransport) + " - Can't Send to client, client not connected, clientId: " + clientId);
                }
            }
        }

        public override void Shutdown()
        {
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                UnityEngine.Debug.Log(nameof(SteamNetworkingTransport) + " - Shutdown");

            if (_p2PSessionRequestCallback != null)
                _p2PSessionRequestCallback.Dispose();
            if (_p2PSessionConnectFailCallback != null)
                _p2PSessionConnectFailCallback.Dispose();

            sendPings = false;
            isServer = false;
            connectionAttemptFailed = false;

            sentPings.Clear();
            pingIdCounter = 0;

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.StartCoroutine(Delay(100, () =>
                {
                    //Need to delay the closing of the p2p sessions to not block the disconect message before it is sent.
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
            serverUser = new User(new CSteamID(ConnectToSteamID));

#if UNITY_SERVER
            if (SteamGameServerNetworking.SendP2PPacket(serverUser.SteamId, new byte[] { 0 }, 1, EP2PSend.k_EP2PSendReliable, (int)InternalChannelType.Connect))
#else

            if (SteamNetworking.SendP2PPacket(serverUser.SteamId, new byte[] { 0 }, 1, EP2PSend.k_EP2PSendReliable, (int)InternalChannelType.Connect))
#endif
            {
                _p2PSessionConnectFailCallback = Callback<P2PSessionConnectFail_t>.Create((sessionConnectFailInfo) =>
                {
                    OnP2PSessionConnectFail(sessionConnectFailInfo);
                });
            }
            else
            {
                P2PSessionConnectFail_t sessionConnectFailInfo = new P2PSessionConnectFail_t()
                {
                    m_eP2PSessionError = (byte)EP2PSessionError.k_EP2PSessionErrorMax,
                    m_steamIDRemote = serverUser.SteamId
                };

                OnP2PSessionConnectFail(sessionConnectFailInfo);

                return false;
            }

            return true;
        }

        public override bool StartServer()
        {
            isServer = true;

            // setup the callback method
            _p2PSessionRequestCallback = Callback<P2PSessionRequest_t>.Create(OnP2PSessionRequest);
            _p2PSessionConnectFailCallback = Callback<P2PSessionConnectFail_t>.Create(OnP2PSessionConnectFail);
            OnConnected();

            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                UnityEngine.Debug.Log(nameof(SteamNetworkingTransport) + " - StartServer");

            return true;
        }

        private EP2PSend NetworkDeliveryToEP2PSend(NetworkDelivery type)
        {
            EP2PSend options = EP2PSend.k_EP2PSendReliableWithBuffering;
            switch (type)
            {
                case NetworkDelivery.Unreliable:
                    options = EP2PSend.k_EP2PSendUnreliable;
                    break;
                case NetworkDelivery.UnreliableSequenced:
                    options = EP2PSend.k_EP2PSendUnreliable;
                    break;
                case NetworkDelivery.Reliable:
                    options = EP2PSend.k_EP2PSendReliableWithBuffering;
                    break;
                case NetworkDelivery.ReliableSequenced:
                    options = EP2PSend.k_EP2PSendReliableWithBuffering;
                    break;
                case NetworkDelivery.ReliableFragmentedSequenced:
                    options = EP2PSend.k_EP2PSendReliableWithBuffering;
                    break;
                default:
                    options = EP2PSend.k_EP2PSendReliableWithBuffering;
                    break;
            }

            return options;
        }

        private void CloseP2PSessions()
        {
            foreach (User user in connectedUsers.Values)
            {
#if UNITY_SERVER
                SteamGameServerNetworking.CloseP2PSessionWithUser(user.SteamId);
#else
                SteamNetworking.CloseP2PSessionWithUser(user.SteamId);
#endif
            }

            if (serverUser != null)
            {
#if UNITY_SERVER
                SteamGameServerNetworking.CloseP2PSessionWithUser(serverUser.SteamId);
#else
                SteamNetworking.CloseP2PSessionWithUser(serverUser.SteamId);
#endif
            }

            connectedUsers.Clear();
            serverUser = null;
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                UnityEngine.Debug.Log(nameof(SteamNetworkingTransport) + " - CloseP2PSessions - has Closed P2P Sessions With all Users");
        }

        private void OnP2PSessionRequest(P2PSessionRequest_t request)
        {
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                NetworkLog.LogInfoServer(nameof(SteamNetworkingTransport) + " - OnP2PSessionRequest - m_steamIDRemote: " + request.m_steamIDRemote);

            CSteamID userId = request.m_steamIDRemote;

            //Todo: Might want to check if we expect the user before just accepting it.
#if UNITY_SERVER
            SteamGameServerNetworking.AcceptP2PSessionWithUser(userId);
#else
            SteamNetworking.AcceptP2PSessionWithUser(userId);
#endif
        }

        private void OnP2PSessionConnectFail(P2PSessionConnectFail_t request)
        {
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer)
                NetworkLog.LogInfoServer(nameof(SteamNetworkingTransport) + " - OnP2PSessionConnectFail - m_steamIDRemote: " + request.m_eP2PSessionError.ToString() + " Error: " + request.m_eP2PSessionError.ToString());
            connectionAttemptFailed = true;
            connectionAttemptFailedClientId = request.m_steamIDRemote.m_SteamID;
        }

        private static IEnumerator Delay(int milliseconds, Action action)
        {
            yield return new WaitForSeconds(milliseconds / 1000f);
            action.Invoke();
        }

        private void OnConnected()
        {
            StartPingSendingLoop();
        }

        private async void StartPingSendingLoop()
        {
            await PingSendingLoop();
        }

        private async Task PingSendingLoop()
        {
            sendPings = true;
            while (sendPings)
            {
                pingIdCounter = (byte)((pingIdCounter + 1) % 128);
                sentPings.Remove(pingIdCounter);
                sentPings.Add(pingIdCounter, new PingTracker());

                pingPongMessageBuffer[0] = pingIdCounter;

                if (isServer)
                {
                    foreach (User user in connectedUsers.Values)
                    {
#if UNITY_SERVER
                        SteamGameServerNetworking.SendP2PPacket(user.SteamId, pingPongMessageBuffer, (uint)pingPongMessageBuffer.Length, EP2PSend.k_EP2PSendUnreliableNoDelay, (int)InternalChannelType.Ping);
#else
                        SteamNetworking.SendP2PPacket(user.SteamId, pingPongMessageBuffer, (uint)pingPongMessageBuffer.Length, EP2PSend.k_EP2PSendUnreliableNoDelay, (int)InternalChannelType.Ping);
#endif
                    }
                }
                else
                {
#if UNITY_SERVER
                    SteamGameServerNetworking.SendP2PPacket(serverUser.SteamId, pingPongMessageBuffer, (uint)pingPongMessageBuffer.Length, EP2PSend.k_EP2PSendUnreliableNoDelay, (int)InternalChannelType.Ping);
#else
                    SteamNetworking.SendP2PPacket(serverUser.SteamId, pingPongMessageBuffer, (uint)pingPongMessageBuffer.Length, EP2PSend.k_EP2PSendUnreliableNoDelay, (int)InternalChannelType.Ping);
#endif
                }

                await Task.Delay(TimeSpan.FromSeconds(PingInterval));
            }
        }
    }
}
#endif
