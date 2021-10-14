#if !DISABLESTEAMWORKS
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Steamworks;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Reflection;
using Unity.Netcode;

/*
 * Steamworks API Reference for ISteamNetworking: https://partner.steamgames.com/doc/api/ISteamNetworking
 * Steamworks.NET: https://steamworks.github.io/
 */

namespace Netcode.Transports.SteamP2P
{
    public class SteamP2PTransport : NetworkTransport
    {
        private Callback<P2PSessionRequest_t> _p2PSessionRequestCallback;
        private Callback<P2PSessionConnectFail_t> _p2PSessionConnectFailCallback;

        public ulong ConnectToSteamID;

        private class User
        {
            public User(CSteamID steamId)
            {
                SteamId = steamId;
                ClientId = SteamId.m_SteamID;
            }
            public CSteamID SteamId;
            public ulong ClientId;
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
            InternalChannelsCount = 4,
            NetcodeData = 5, // channel used to transfer data for Netcode for GameObjects
        }

        private int channelCounter = 0;
        private int currentPollChannel = 0;

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


        public override ulong ServerClientId
        {
            get
            {
                return 0;
            }
        }

        public override void DisconnectLocalClient()
        {
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer) NetworkLog.LogInfoServer("SteamP2PTransport - DisconnectLocalClient");
            SteamNetworking.SendP2PPacket(serverUser.SteamId, new byte[] { 0 }, 1, EP2PSend.k_EP2PSendReliable, (int)InternalChannelType.Disconnect);
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer) NetworkLog.LogInfoServer("SteamP2PTransport - DisconnectRemoteClient clientId: " + clientId);

            if (!connectedUsers.ContainsKey(clientId))
            {
                if (NetworkManager.Singleton.LogLevel <= LogLevel.Error) NetworkLog.LogErrorServer("SteamP2PTransport - Can't disconect client, client not connected, clientId: " + clientId);
                return;
            }

            SteamNetworking.SendP2PPacket(connectedUsers[clientId].SteamId, new byte[] { 0 }, 1, EP2PSend.k_EP2PSendReliable, (int)InternalChannelType.Disconnect);
            CSteamID steamId = connectedUsers[clientId].SteamId;

            NetworkManager.Singleton.StartCoroutine(Delay(100, () =>
            { //Need to delay the closing of the p2p sessions to not block the disconect message before it is sent.
                SteamNetworking.CloseP2PSessionWithUser(steamId);
                if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer) NetworkLog.LogInfoServer("SteamP2PTransport - DisconnectRemoteClient - has Closed P2P Session With clientId: " + clientId);
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
                    if (NetworkManager.Singleton.LogLevel <= LogLevel.Error) NetworkLog.LogErrorServer("SteamP2PTransport - Can't GetCurrentRtt from client, client not connected, clientId: " + clientId);
                }
            }
            else
            {
                return serverUser.Ping.Get();
            }
            return 0ul;
        }

        public override void Initialize()
        {
            Type steamManagerType = Type.GetType("SteamManager");

            PropertyInfo property = steamManagerType == null ? null : steamManagerType.GetProperty("Initialized", BindingFlags.Static | BindingFlags.Public);

            if (steamManagerType == null || property == null || !property.CanRead || property.PropertyType != typeof(bool))
            {
                if (NetworkManager.Singleton.LogLevel <= LogLevel.Error) NetworkLog.LogErrorServer("SteamP2PTransport - Init - Steamworks.NET SteamManager.Initialized not found, SteamP2PTransport can not run without it");
                return;
            }

            bool propertyValue = (bool)property.GetValue(null);

            if (!propertyValue)
            {
                if (NetworkManager.Singleton.LogLevel <= LogLevel.Error) NetworkLog.LogErrorServer("SteamP2PTransport - Init - Steamworks.NET is not Initialized, SteamP2PTransport can not run without it");
                return;
            }
            
            channelCounter = 0;
            currentPollChannel = 0;

            // Add SteamP2PTransport internal channels
            for (int i = 0; i < (int)InternalChannelType.InternalChannelsCount; i++)
            {
                int channelId = AddChannel(EP2PSend.k_EP2PSendReliableWithBuffering);
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

            while (currentPollChannel < channelCounter)
            {
                if (SteamNetworking.IsP2PPacketAvailable(out uint msgSize, currentPollChannel))
                {
                    uint bytesRead;
                    CSteamID remoteId;
                    if (messageBuffer.Length < msgSize)
                    {
                        messageBuffer = new byte[msgSize];
                        if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer) NetworkLog.LogInfoServer("SteamP2PTransport - PollEvent - Increase buffer size to: " + msgSize);
                    }

                    if (SteamNetworking.ReadP2PPacket(messageBuffer, msgSize, out bytesRead, out remoteId, currentPollChannel))
                    {
                        clientId = remoteId.m_SteamID;

                        if (currentPollChannel < (int)InternalChannelType.InternalChannelsCount)
                        {
                            payload = new ArraySegment<byte>();

                            switch (currentPollChannel)
                            {
                                case (byte)InternalChannelType.Disconnect:

                                    connectedUsers.Remove(clientId);
                                    SteamNetworking.CloseP2PSessionWithUser(remoteId);
                                    receiveTime = Time.realtimeSinceStartup;
                                    return NetworkEvent.Disconnect;

                                case (byte)InternalChannelType.Connect:

                                    if (isServer)
                                    {
                                        SteamNetworking.SendP2PPacket(remoteId, new byte[] { 0 }, 1, EP2PSend.k_EP2PSendReliable, (int)InternalChannelType.Connect);
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
                                    SteamNetworking.SendP2PPacket(remoteId, pingPongMessageBuffer, msgSize, EP2PSend.k_EP2PSendUnreliableNoDelay, (int)InternalChannelType.Pong);
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

                            }

                        }
                        else
                        {
                            payload = new ArraySegment<byte>(messageBuffer, 0, (int)msgSize);
                            receiveTime = Time.realtimeSinceStartup;
                            return NetworkEvent.Data;
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
                SteamNetworking.SendP2PPacket(serverUser.SteamId, data.Array, (uint)data.Count, sendType, (int)InternalChannelType.NetcodeData);
            }
            else
            {
                if (connectedUsers.ContainsKey(clientId))
                {
                    SteamNetworking.SendP2PPacket(connectedUsers[clientId].SteamId, data.Array, (uint)data.Count, sendType, (int)InternalChannelType.NetcodeData);
                }
                else
                {
                    if (NetworkManager.Singleton.LogLevel <= LogLevel.Error) NetworkLog.LogErrorServer("SteamP2PTransport - Can't Send to client, client not connected, clientId: " + clientId);
                }
            }
        }

        public override void Shutdown()
        {
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer) NetworkLog.LogInfoServer("SteamP2PTransport - Shutdown");

            if (_p2PSessionRequestCallback != null)
                _p2PSessionRequestCallback.Dispose();
            if (_p2PSessionConnectFailCallback != null)
                _p2PSessionConnectFailCallback.Dispose();

            sendPings = false;
            isServer = false;
            connectionAttemptFailed = false;
            channelCounter = 0;
            currentPollChannel = 0;

            sentPings.Clear();
            pingIdCounter = 0;

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.StartCoroutine(Delay(100, () =>
                {//Need to delay the closing of the p2p sessions to not block the disconect message before it is sent.
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

            if (SteamNetworking.SendP2PPacket(serverUser.SteamId, new byte[] { 0 }, 1, EP2PSend.k_EP2PSendReliable, (int)InternalChannelType.Connect))
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

            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer) NetworkLog.LogInfoServer("SteamP2PTransport - StartServer - ConnectToCSteamID: " + SteamUser.GetSteamID().m_SteamID.ToString());

            return true;
        }

        private int AddChannel(EP2PSend send)
        {
            channelCounter++;
            return channelCounter - 1;
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
                SteamNetworking.CloseP2PSessionWithUser(user.SteamId);
            }
            if (serverUser != null)
            {
                SteamNetworking.CloseP2PSessionWithUser(serverUser.SteamId);
            }
            connectedUsers.Clear();
            serverUser = null;
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer) NetworkLog.LogInfoServer("SteamP2PTransport - CloseP2PSessions - has Closed P2P Sessions With all Users");
        }

        private void OnP2PSessionRequest(P2PSessionRequest_t request)
        {
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer) NetworkLog.LogInfoServer("SteamP2PTransport - OnP2PSessionRequest - m_steamIDRemote: " + request.m_steamIDRemote);

            CSteamID userId = request.m_steamIDRemote;
            //Todo: Might want to check if we expect the user before just accepting it.
            SteamNetworking.AcceptP2PSessionWithUser(userId);
        }

        private void OnP2PSessionConnectFail(P2PSessionConnectFail_t request)
        {
            if (NetworkManager.Singleton.LogLevel <= LogLevel.Developer) NetworkLog.LogInfoServer("SteamP2PTransport - OnP2PSessionConnectFail - m_steamIDRemote: " + request.m_eP2PSessionError.ToString() + " Error: " + request.m_eP2PSessionError.ToString());
            connectionAttemptFailed = true;
            connectionAttemptFailedClientId = request.m_steamIDRemote.m_SteamID;
            InvokeOnTransportEvent(NetworkEvent.Disconnect, 0ul, default, Time.realtimeSinceStartup);
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
                        SteamNetworking.SendP2PPacket(user.SteamId, pingPongMessageBuffer, (uint)pingPongMessageBuffer.Length, EP2PSend.k_EP2PSendUnreliableNoDelay, (int)InternalChannelType.Ping);
                    }
                }
                else
                {
                    SteamNetworking.SendP2PPacket(serverUser.SteamId, pingPongMessageBuffer, (uint)pingPongMessageBuffer.Length, EP2PSend.k_EP2PSendUnreliableNoDelay, (int)InternalChannelType.Ping);
                }

                await Task.Delay(TimeSpan.FromSeconds(PingInterval));
            }
        }
    }
}
#endif
