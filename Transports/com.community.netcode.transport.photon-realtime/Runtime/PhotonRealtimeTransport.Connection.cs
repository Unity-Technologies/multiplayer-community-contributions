using System.Collections.Generic;
using Photon.Realtime;
using System;
using Unity.Netcode;
using UnityEngine;

namespace Netcode.Transports.PhotonRealtime
{
    public partial class PhotonRealtimeTransport : IConnectionCallbacks
    {
        public void OnConnected()
        {
        }

        public void OnConnectedToMaster()
        {
            // Once the client does connect to the master immediately redirect to its room.
            var enterRoomParams = new EnterRoomParams()
            {
                RoomName = m_RoomName,
                RoomOptions = new RoomOptions()
                {
                    MaxPlayers = m_MaxPlayers,
                }
            };

            var success = m_IsHostOrServer ? m_Client.OpCreateRoom(enterRoomParams) : m_Client.OpJoinRoom(enterRoomParams);

            if (!success)
            {
                Debug.LogWarning("Unable to create or join room.");
                InvokeTransportEvent(NetworkEvent.Disconnect);
            }
        }

        public void OnCustomAuthenticationFailed(string debugMessage)
        {
        }

        public void OnCustomAuthenticationResponse(Dictionary<string, object> data)
        {
        }

        public void OnDisconnected(DisconnectCause cause)
        {
            InvokeTransportEvent(NetworkEvent.Disconnect);
            this.DeInitialize();
        }

        public void OnRegionListReceived(RegionHandler regionHandler)
        {
        }
    }
}
