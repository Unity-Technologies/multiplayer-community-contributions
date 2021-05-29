using System;
using System.Net;
using MLAPI;
using MLAPI.Transports.UNET;
using UnityEngine;
using UnityEngine.Events;
using Random = UnityEngine.Random;

[RequireComponent(typeof(NetworkManager))]
public class NetworkDiscovery : NetworkDiscoveryBase<DiscoveryBroadcastData, DiscoveryResponseData>
{
    [Serializable]
    public class ServerFoundEvent : UnityEvent<IPEndPoint, DiscoveryResponseData>
    {
    };

    NetworkManager m_NetworkManager;

    public ServerFoundEvent OnServerFound;

    [SerializeField]
    [Tooltip("If true NetworkDiscovery will make the server visible and answer to client broadcasts as soon as MLAPI starts running as server.")]
    bool m_StartWithServer = true;

    private bool m_HasStartedWithServer = false;

    public void Awake()
    {
        m_NetworkManager = GetComponent<NetworkManager>();
    }

    public void Update()
    {
        if (m_StartWithServer && m_HasStartedWithServer == false && IsRunning == false)
        {
            if (m_NetworkManager.IsServer)
            {
                StartServer();
                m_HasStartedWithServer = true;
            }
        }
    }

    protected override bool ProcessBroadcast(IPEndPoint sender, DiscoveryBroadcastData broadCast, out DiscoveryResponseData response)
    {
        response = new DiscoveryResponseData()
        {
            Port = (ushort) ((UNetTransport) m_NetworkManager.NetworkConfig.NetworkTransport).ConnectPort,
            ServerName = "Foo"
        };
        return true;
    }

    protected override void ResponseReceived(IPEndPoint sender, DiscoveryResponseData response)
    {
        OnServerFound.Invoke(sender, response);
    }
}