using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

namespace Netcode.Transports.MultipeerConnectivity
{
    public class MultipeerConnectivityTransport : NetworkTransport
    {
        /// <summary>
        /// This class is a singleton so it's easy to be referenced anywhere.
        /// </summary>
        public static MultipeerConnectivityTransport Instance => s_instance;

        private static MultipeerConnectivityTransport s_instance;

        /// <summary>
        /// The server client Id should always be 0.
        /// </summary>
        public override ulong ServerClientId => 0;

        [Tooltip("This is a unique Id to identify your MPC session. Only devices with the same session Id can connect to each other. " +
            "You can leave this to empty but it will make your network session not unique.")]
        public string SessionId = null;

        [Tooltip("This will be the name of your device in the network.")]
        public string Nickname = "yuchen";

        [Header("Host Config")]
        [Tooltip("Setting this to true to automatically advertise after starting host. " +
            "Otherwise, you will need to manually call StartAdvertising().")]
        public bool AutoAdvertise = true;

        [Tooltip("Setting this to true to automatically approve all incoming connection requests. " +
            "Otherwise, you will need to manually approve each connection request.")]
        public bool AutoApproveConnectionRequest = true;

        [Header("Client Config")]
        [Tooltip("Setting this to true to automatically browse after starting client. " +
            "Otherwise, you will need to manually call StartBrowsing().")]
        public bool AutoBrowse = true;

        [Tooltip("Setting this to true to automatically join the first browsed session. " +
            "Otherwise, you will need to manually send connection request to a host.")]
        public bool AutoSendConnectionRequest = true;

        public Dictionary<int, string> NearbyHostDict => _nearbyHostDict;

        public Dictionary<int, string> PendingConnectionRequestDict => _pendingConnectionRequestDict;

        public bool IsAdvertising => _isAdvertising;

        public bool IsBrowsing => _isBrowsing;
        
        /// <summary>
        /// Showing whether the device is currently advertising itself.
        /// </summary>
        private bool _isAdvertising = false;

        /// <summary>
        /// Showing whether the device is currently browsing for nearby peers.
        /// </summary>
        private bool _isBrowsing = false;

        /// <summary>
        /// Stores all browsed nearby hosts. The first parameter is the browsed host key
        /// and the second is the browsed host name.
        /// </summary>
        private readonly Dictionary<int, string> _nearbyHostDict = new();

        /// <summary>
        /// Stores all received connection requests. The first parameter is the connection request key
        /// and the second is the name of the client who sent the connection request.
        /// </summary>
        private readonly Dictionary<int, string> _pendingConnectionRequestDict = new();

        /// <summary>
        /// Check if we are currently running on an iOS device.
        /// </summary>
        public static bool IsRuntime => Application.platform == RuntimePlatform.IPhonePlayer;

        /// <summary>
        /// Initialize the MPCSession and register native callbacks.
        /// </summary>
        /// <param name="nickname">The name of the device displayed in the network</param>
        /// <param name="onBrowserFoundPeer">Invoked when the browser finds a peer</param>
        /// <param name="onBrowserLostPeer">Invoked when the browser loses a peer</param>
        /// <param name="onAdvertiserReceivedConnectionRequest">Invoked when the advertiser receives a connection request</param>
        /// <param name="onAdvertiserApprovedConnectionRequest">Invoked when the advertiser approves a connection request</param>
        /// <param name="onConnectingWithPeer">Invoked when connecting with a peer</param>
        /// <param name="onConnectedWithPeer">Invoked when connected with a peer</param>
        /// <param name="onDisconnectedWithPeer">Invoked when disconnected with a peer</param>
        /// <param name="onReceivedData">Invoked when receives data message from a peer</param>
        [DllImport("__Internal")]
        private static extern void MPC_Initialize(string nickname,
                                                  Action<int, string> onBrowserFoundPeer,
                                                  Action<int, string> onBrowserLostPeer,
                                                  Action<int, string> onAdvertiserReceivedConnectionRequest,
                                                  Action<int> onAdvertiserApprovedConnectionRequest,
                                                  Action<string> onConnectingWithPeer,
                                                  Action<int, string> onConnectedWithPeer,
                                                  Action<int, string> onDisconnectedWithPeer,
                                                  Action<int, IntPtr, int> onReceivedData);

        /// <summary>
        /// Start advertising to allow nearby peers to find you.
        /// </summary>
        /// <param name="sessionId">The unique id of the network session</param>
        /// <param name="autoApproveConnectionRequest">Setting to true to approve all incoming connection requests</param>
        [DllImport("__Internal")]
        private static extern void MPC_StartAdvertising(string sessionId, bool autoApproveConnectionRequest);

        /// <summary>
        /// Start browsing for nearny advertising peers.
        /// </summary>
        /// <param name="sessionId">The unique id of the network session</param>
        /// <param name="autoSendConnectionRequest">Setting to true to automatically send connection request to the first browsed peer</param>
        [DllImport("__Internal")]
        private static extern void MPC_StartBrowsing(string sessionId, bool autoSendConnectionRequest);

        /// <summary>
        /// Stop advertising.
        /// </summary>
        [DllImport("__Internal")]
        private static extern void MPC_StopAdvertising();

        /// <summary>
        /// Stop browsing.
        /// </summary>
        [DllImport("__Internal")]
        private static extern void MPC_StopBrowsing();

        /// <summary>
        /// Shutdown and deinitialize the MPCSession.
        /// </summary>
        [DllImport("__Internal")]
        private static extern void MPC_Shutdown();

        /// <summary>
        /// Send data message to a specific connected peer.
        /// </summary>
        /// <param name="transportID">The transport id of the recipient peer</param>
        /// <param name="data">The raw data</param>
        /// <param name="length">The length of the data</param>
        /// <param name="reliable">Whether to use realiable way to send the data</param>
        [DllImport("__Internal")]
        private static extern void MPC_SendData(int transportID, byte[] data, int length, bool reliable);

        /// <summary>
        /// Send connection request to a specific browsed host.
        /// </summary>
        /// <param name="nearbyHostKey">The key of the host in the dict</param>
        [DllImport("__Internal")]
        private static extern void MPC_SendConnectionRequest(int nearbyHostKey);

        /// <summary>
        /// Approve the connection request sent by a specific client.
        /// </summary>
        /// <param name="connectionRequestKey">The key of the connection request in the dict</param>
        [DllImport("__Internal")]
        private static extern void MPC_ApproveConnectionRequest(int connectionRequestKey);

        /// <summary>
        /// Links to a native callback which is invoked when the browser finds a new nearby host host.
        /// </summary>
        /// <param name="nearbyHostKey">The key of the host in the dict</param>
        /// <param name="nearbyHostName">The name of the host</param>
        [AOT.MonoPInvokeCallback(typeof(Action<int, string>))]
        private static void OnBrowserFoundPeerDelegate(int nearbyHostKey, string nearbyHostName)
        {
            if (s_instance != null)
            {
                // Add browsed host to the dict
                s_instance._nearbyHostDict.Add(nearbyHostKey, nearbyHostName);
                // Invoke the event
                s_instance.OnBrowserFoundPeer?.Invoke(nearbyHostKey, nearbyHostName);
            } 
        }

        /// <summary>
        /// Links to a native callback which is invoked when the browser loses a host.
        /// </summary>
        /// <param name="nearbyHostKey">The key of the host in the dict</param>
        /// <param name="nearbyHostName">The name of the host</param>
        [AOT.MonoPInvokeCallback(typeof(Action<int, string>))]
        private static void OnBrowserLostPeerDelegate(int nearbyHostKey, string nearbyHostName)
        {
            if (s_instance != null)
            {
                // Remove browsed host from the dict
                s_instance._nearbyHostDict.Remove(nearbyHostKey);
                // Invoke the event
                s_instance.OnBrowserLostPeer?.Invoke(nearbyHostKey, nearbyHostName);
            }
        }

        /// <summary>
        /// Links to a native callback which is invoked when the advertiser receives a connection request.
        /// </summary>
        /// <param name="connectionRequestKey">The key of the connection request in the dict</param>
        /// <param name="senderName">The name of the client who sent the connection request</param>
        [AOT.MonoPInvokeCallback(typeof(Action<int, string>))]
        private static void OnAdvertiserReceivedConnectionRequestDelegate(int connectionRequestKey, string senderName)
        {
            if (s_instance != null)
            {
                if (!s_instance.AutoApproveConnectionRequest)
                {
                    // Add connection request to the dict
                    s_instance._pendingConnectionRequestDict.Add(connectionRequestKey, senderName);
                }
                // Invoke the event
                s_instance.OnAdvertiserReceivedConnectionRequest?.Invoke(connectionRequestKey, senderName);
            }
        }

        /// <summary>
        /// Links to a native callback which is invoked when the advertiser handles a connection request.
        /// </summary>
        /// <param name="connectionRequestKey">The key of the connection request in the dict</param>
        [AOT.MonoPInvokeCallback(typeof(Action<int>))]
        private static void OnAdvertiserApprovedConnectionRequestDelegate(int connectionRequestKey)
        {
            if (s_instance != null)
            {
                if (s_instance._pendingConnectionRequestDict.ContainsKey(connectionRequestKey))
                {
                    // Remove the connection request from the dict
                    s_instance._pendingConnectionRequestDict.Remove(connectionRequestKey);
                }
                // Invoke the event
                s_instance.OnAdvertiserApprovedConnectionRequest?.Invoke(connectionRequestKey);
            }
        }

        /// <summary>
        /// Links to a native callback which is invoked when the local peer is connecting with a peer.
        /// </summary>
        /// <param name="peerName">The name of the peer</param>
        [AOT.MonoPInvokeCallback(typeof(Action<string>))]
        private static void OnConnectingWithPeerDelegate(string peerName)
        {
            if (s_instance != null)
            {
                s_instance.OnConnectingWithPeer?.Invoke(peerName);
            }
        }

        /// <summary>
        /// Links to a native callback which is invoked when the local peer is connected with a peer.
        /// </summary>
        /// <param name="transportID">The transport id of the peer</param>
        /// <param name="peerName">The name of the peer</param>
        [AOT.MonoPInvokeCallback(typeof(Action<int, string>))]
        private static void OnConnectedWithPeerDelegate(int transportID, string peerName)
        {
            if (s_instance != null)
            {
                s_instance._isBrowsing = false;
                s_instance._nearbyHostDict.Clear();

                s_instance.InvokeOnTransportEvent(NetworkEvent.Connect, (ulong)transportID,
                    default, Time.realtimeSinceStartup);
            }
        }

        /// <summary>
        /// Links to a native callback which is invoked when the local peer is disconnected with a peer.
        /// </summary>
        /// <param name="transportID">The transport id of the peer</param>
        /// <param name="peerName">The name of the peer</param>
        [AOT.MonoPInvokeCallback(typeof(Action<int, string>))]
        private static void OnDisconnectedWithPeerDelegate(int transportID, string peerName)
        {
            if (s_instance != null)
            {
                s_instance.InvokeOnTransportEvent(NetworkEvent.Disconnect, (ulong)transportID,
                   default, Time.realtimeSinceStartup);
            }
        }

        /// <summary>
        /// Links to a native callback which is invoked when the local peer receives data from a peer.
        /// </summary>
        /// <param name="transportID">The transport id of the peer</param>
        /// <param name="dataPtr">The pointer to the raw data</param>
        /// <param name="length">The length of the data array</param>
        [AOT.MonoPInvokeCallback(typeof(Action<int, IntPtr, int>))]
        private static void OnReceivedDataDelegate(int transportID, IntPtr dataPtr, int length)
        {
            if (s_instance != null)
            {
                byte[] data = new byte[length];
                Marshal.Copy(dataPtr, data, 0, length);
                s_instance.InvokeOnTransportEvent(NetworkEvent.Data, (ulong)transportID,
                    new ArraySegment<byte>(data, 0, length), Time.realtimeSinceStartup);
            }
        }

        /// <summary>
        /// Invoked when the browser finds a nearby host peer.
        /// The first parameter is the host peer key in the dict.
        /// The second parameter is the name of the host peer.
        /// </summary>
        public event Action<int, string> OnBrowserFoundPeer;

        /// <summary>
        /// Invoked when the browser loses a nearby host peer.
        /// The first parameter is the host peer key in the dict.
        /// The second parameter is the name of the host peer.
        /// </summary>
        public event Action<int, string> OnBrowserLostPeer;

        /// <summary>
        /// Invoked when the advertiser receives a connection request.
        /// The first parameter is the connection request key in the dict.
        /// The second parameter is the name of the peer who sent the connection request.
        /// </summary>
        public event Action<int, string> OnAdvertiserReceivedConnectionRequest;

        public event Action<int> OnAdvertiserApprovedConnectionRequest;

        /// <summary>
        /// Invoked when initializes connection with a new peer. This event should be used only for notification purpose.
        /// The first parameter is the name of the connecting peer.
        /// </summary>
        public event Action<string> OnConnectingWithPeer;

        private void Awake()
        {
            // Initialize the singleton instance
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
            }
            else
            {
                s_instance = this;
            }
        }

        public override void Initialize(NetworkManager networkManager)
        {
            if (!IsRuntime)
            {
                Debug.LogError($"[MPCTransport] MPCTransport cannot run in Unity Editor, it can only run on an iOS device. " +
                    $"If you want to test your project in Unity Editor, please use Unity Transport instead when debugging.");
                return;
            }

            MPC_Initialize(Nickname,
                           OnBrowserFoundPeerDelegate,
                           OnBrowserLostPeerDelegate,
                           OnAdvertiserReceivedConnectionRequestDelegate,
                           OnAdvertiserApprovedConnectionRequestDelegate,
                           OnConnectingWithPeerDelegate,
                           OnConnectedWithPeerDelegate,
                           OnDisconnectedWithPeerDelegate,
                           OnReceivedDataDelegate);
        }

        public override bool StartServer()
        {
            if (AutoAdvertise)
            {
                StartAdvertising();
            }
            return true;
        }

        public override bool StartClient()
        {
            if (AutoBrowse)
            {
                StartBrowsing();
            }
            return true;
        }

        public override NetworkEvent PollEvent(out ulong transportId, out ArraySegment<byte> payload, out float receiveTime)
        {
            transportId = 0;
            payload = default;
            receiveTime = Time.realtimeSinceStartup;
            return NetworkEvent.Nothing;
        }

        public override void Send(ulong transportId, ArraySegment<byte> data, NetworkDelivery networkDelivery)
        {
            MPC_SendData((int)transportId, data.Array, data.Count,
                !(networkDelivery == NetworkDelivery.Unreliable || networkDelivery == NetworkDelivery.UnreliableSequenced));
        }

        public override ulong GetCurrentRtt(ulong transportId)
        {
            return 0;
        }

        /// <summary>
        /// Called when a client tries to disconnect from the server.
        /// </summary>
        public override void DisconnectLocalClient()
        {

        }

        public override void DisconnectRemoteClient(ulong transportId)
        {

        }

        public override void Shutdown()
        {
            if (IsRuntime)
            {
                MPC_Shutdown();
                // Reset variables
                _pendingConnectionRequestDict.Clear();
                _nearbyHostDict.Clear();
                _isAdvertising = false;
                _isBrowsing = false;
            }
        }

        /// <summary>
        /// Start advertising.
        /// </summary>
        public void StartAdvertising()
        {
            if (IsRuntime && !_isAdvertising)
            {
                _pendingConnectionRequestDict.Clear();
                MPC_StartAdvertising(SessionId, AutoApproveConnectionRequest);
                _isAdvertising = true;
            }
        }

        /// <summary>
        /// Stop advertising.
        /// </summary>
        public void StopAdvertising()
        {
            if (IsRuntime && _isAdvertising)
            {
                MPC_StopAdvertising();
                _isAdvertising = false;
                _pendingConnectionRequestDict.Clear();
            }
        }

        /// <summary>
        /// Start browsing.
        /// </summary>
        public void StartBrowsing()
        {
            if (IsRuntime && !_isBrowsing)
            {
                _nearbyHostDict.Clear();
                MPC_StartBrowsing(SessionId, AutoSendConnectionRequest);
                _isBrowsing = true;
            }
        }

        /// <summary>
        /// Stop browsing.
        /// </summary>
        public void StopBrowsing()
        {
            if (IsRuntime && _isBrowsing)
            {
                MPC_StopBrowsing();
                _isBrowsing = false;
                _nearbyHostDict.Clear();
            }
        }

        public void SendConnectionRequest(int nearbyHostKey)
        {
            if (IsRuntime)
            {
                MPC_SendConnectionRequest(nearbyHostKey);
            }
        }

        public void ApproveConnectionRequest(int connectionRequestKey)
        {
            if (IsRuntime)
            {
                MPC_ApproveConnectionRequest(connectionRequestKey);
            }
        }
    }
}