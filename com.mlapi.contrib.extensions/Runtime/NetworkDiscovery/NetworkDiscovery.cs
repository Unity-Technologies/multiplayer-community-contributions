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
    public class ServerFoundEvent : UnityEvent<IPEndPoint, DiscoveryResponseData> { };

    NetworkManager m_NetworkManager;

    // This is long because unity inspector does not like ulong (╯°□°）╯︵ ┻━┻
    public long UniqueApplicationId;

    public ServerFoundEvent OnServerFound;

    [SerializeField]
    [Tooltip("If true NetworkDiscovery will make the server visible and answer to client broadcasts as soon as MLAPI starts running as server.")]
    bool m_StartWithServer = true;

    public void Awake()
    {
        m_NetworkManager = GetComponent<NetworkManager>();
    }

    public void Update()
    {
        if (m_StartWithServer && IsRunning == false)
        {
            if (m_NetworkManager.IsServer)
            {
                StartServer();
            }
        }
    }

    void OnValidate()
    {
        if (UniqueApplicationId == 0)
        {
            var value1 = (long)Random.Range(int.MinValue, int.MaxValue);
            var value2 = (long)Random.Range(int.MinValue, int.MaxValue);
            UniqueApplicationId =  value1 + (value2 << 32);
        }
    }

    protected override bool ProcessBroadcast(IPEndPoint sender, DiscoveryBroadcastData broadCast, out DiscoveryResponseData response)
    {
        if (broadCast.UniqueApplicationId == UniqueApplicationId)
        {
            response = new DiscoveryResponseData()
            {
                Port = (ushort)((UNetTransport)m_NetworkManager.NetworkConfig.NetworkTransport).ConnectPort,
                UniqueApplicationId = UniqueApplicationId,
                ServerName = "Foo"
            };
            return true;
        }

        response = default;
        return false;
    }

    protected override void ResponseReceived(IPEndPoint sender, DiscoveryResponseData response)
    {
        OnServerFound.Invoke(sender, response);
    }
}
