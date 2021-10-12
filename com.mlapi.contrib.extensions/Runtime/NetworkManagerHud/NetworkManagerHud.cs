// using System;
// using System.Runtime.CompilerServices;
// using Unity.Netcode;
// using Unity.Netcode.Transports.UNET;
// using UnityEngine;
//
// namespace Netcode.Extensions
// {
//     [RequireComponent(typeof(NetworkManager))]
//     [DisallowMultipleComponent]
//     public class NetworkManagerHud : MonoBehaviour
//     {
//         NetworkManager m_NetworkManager;
//
//         UnityTransport m_Transport;
//
//         GUIStyle m_LabelTextStyle;
//
//         // This is needed to make the port field more convenient. GUILayout.TextField is very limited and we want to be able to clear the field entirely so we can't cache this as ushort.
//         string m_PortString;
//
//         public Vector2 DrawOffset = new Vector2(10, 10);
//
//         public Color LabelColor = Color.black;
//
//         void Awake()
//         {
//             // Only cache networking manager but not transport here because transport could change anytime.
//             m_NetworkManager = GetComponent<NetworkManager>();
//             m_LabelTextStyle = new GUIStyle(GUIStyle.none);
//         }
//
//         void OnGUI()
//         {
//             m_LabelTextStyle.normal.textColor = LabelColor;
//
//             m_Transport = (UnityTransport)m_NetworkManager.NetworkConfig.NetworkTransport;
//
//             if (m_PortString == null)
//             {
//                 m_PortString = m_Transport.ConnectPort.ToString();
//             }
//
//             GUILayout.BeginArea(new Rect(DrawOffset, new Vector2(200, 200)));
//
//             if (IsRunning(m_NetworkManager))
//             {
//                 DrawStatusGUI();
//             }
//             else
//             {
//                 DrawConnectGUI();
//             }
//
//             GUILayout.EndArea();
//         }
//
//         void DrawConnectGUI()
//         {
//             GUILayout.BeginHorizontal();
//             GUILayout.Space(10);
//             GUILayout.Label("Address", m_LabelTextStyle);
//             GUILayout.Label("Port", m_LabelTextStyle);
//
//             GUILayout.EndHorizontal();
//
//             GUILayout.BeginHorizontal();
//
//             m_Transport.ConnectAddress = GUILayout.TextField(m_Transport.ConnectAddress);
//             m_PortString = GUILayout.TextField(m_PortString);
//             if (ushort.TryParse(m_PortString, out ushort port))
//             {
//                 m_Transport.ConnectPort = port;
//             }
//
//             GUILayout.EndHorizontal();
//
//             if (GUILayout.Button("Host (Server + Client)"))
//             {
//                 m_NetworkManager.StartHost();
//             }
//
//             GUILayout.BeginHorizontal();
//
//             if (GUILayout.Button("Server"))
//             {
//                 m_NetworkManager.StartServer();
//             }
//
//             if (GUILayout.Button("Client"))
//             {
//                 m_NetworkManager.StartClient();
//             }
//
//             GUILayout.EndHorizontal();
//         }
//
//         void DrawStatusGUI()
//         {
//             if (m_NetworkManager.IsServer)
//             {
//                 var mode = m_NetworkManager.IsHost ? "Host" : "Server";
//                 GUILayout.Label($"{mode} active on port: {m_Transport.ConnectPort.ToString()}", m_LabelTextStyle);
//             }
//             else
//             {
//                 if (m_NetworkManager.IsConnectedClient)
//                 {
//                     GUILayout.Label($"Client connected {m_Transport.ConnectAddress}:{m_Transport.ConnectPort.ToString()}", m_LabelTextStyle);
//                 }
//             }
//
//             if (GUILayout.Button("Stop"))
//             {
//                 m_NetworkManager.Shutdown();
//             }
//         }
//         
//         [MethodImpl(MethodImplOptions.AggressiveInlining)]
//         bool IsRunning(NetworkManager networkManager) => networkManager.IsServer || networkManager.IsClient;
//     }
// }
