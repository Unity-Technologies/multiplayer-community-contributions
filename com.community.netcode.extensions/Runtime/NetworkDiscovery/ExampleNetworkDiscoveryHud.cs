using System.Collections.Generic;
using System.Net;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Events;
#endif

[RequireComponent(typeof(ExampleNetworkDiscovery))]
[RequireComponent(typeof(NetworkManager))]
public class ExampleNetworkDiscoveryHud : MonoBehaviour
{
    [SerializeField, HideInInspector]
    ExampleNetworkDiscovery m_Discovery;
    
    NetworkManager m_NetworkManager;

    Dictionary<IPAddress, DiscoveryResponseData> discoveredServers = new Dictionary<IPAddress, DiscoveryResponseData>();

    public Vector2 DrawOffset = new Vector2(10, 210);

    void Awake()
    {
        m_Discovery = GetComponent<ExampleNetworkDiscovery>();
        m_NetworkManager = GetComponent<NetworkManager>();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (m_Discovery == null) // This will only happen once because m_Discovery is a serialize field
        {
            m_Discovery = GetComponent<ExampleNetworkDiscovery>();
            UnityEventTools.AddPersistentListener(m_Discovery.OnServerFound, OnServerFound);
            Undo.RecordObjects(new Object[] { this, m_Discovery}, "Set NetworkDiscovery");
        }
    }
#endif

    void OnServerFound(IPEndPoint sender, DiscoveryResponseData response)
    {
        discoveredServers[sender.Address] = response;
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(DrawOffset, new Vector2(200, 600)));

        if (m_NetworkManager.IsServer || m_NetworkManager.IsClient)
        {
            if (m_NetworkManager.IsServer)
            {
                ServerControlsGUI();
            }
        }
        else
        {
            ClientSearchGUI();
        }

        GUILayout.EndArea();
    }

    void ClientSearchGUI()
    {
        if (m_Discovery.IsRunning)
        {
            if (GUILayout.Button("Stop Client Discovery"))
            {
                m_Discovery.StopDiscovery();
                discoveredServers.Clear();
            }
            
            if (GUILayout.Button("Refresh List"))
            {
                discoveredServers.Clear();
                m_Discovery.ClientBroadcast(new DiscoveryBroadcastData());
            }
            
            GUILayout.Space(40);
            
            foreach (var discoveredServer in discoveredServers)
            {
                if (GUILayout.Button($"{discoveredServer.Value.ServerName}[{discoveredServer.Key.ToString()}]"))
                {
                    UnityTransport transport = (UnityTransport)m_NetworkManager.NetworkConfig.NetworkTransport;
                    transport.SetConnectionData(discoveredServer.Key.ToString(), discoveredServer.Value.Port);
                    m_NetworkManager.StartClient();
                }
            }
        }
        else
        {
            if (GUILayout.Button("Discover Servers"))
            {
                m_Discovery.StartClient();
                m_Discovery.ClientBroadcast(new DiscoveryBroadcastData());
            }
        }
    }

    void ServerControlsGUI()
    {
        if (m_Discovery.IsRunning)
        {
            if (GUILayout.Button("Stop Server Discovery"))
            {
                m_Discovery.StopDiscovery();
            }
        }
        else
        {
            if (GUILayout.Button("Start Server Discovery"))
            {
                m_Discovery.StartServer();
            }
        }
    }
}
