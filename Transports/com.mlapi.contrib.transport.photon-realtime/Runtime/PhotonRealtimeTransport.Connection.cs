using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Realtime;
using System;

namespace MLAPI.Transports.PhotonRealtime
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
                m_ConnectTask.IsDone = true;
                m_ConnectTask.Success = false;
                m_ConnectTask.TransportException = new InvalidOperationException("Unable to create or join room.");
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
            this.DeInit();
        }

        public void OnRegionListReceived(RegionHandler regionHandler)
        {
        }
    }
}
