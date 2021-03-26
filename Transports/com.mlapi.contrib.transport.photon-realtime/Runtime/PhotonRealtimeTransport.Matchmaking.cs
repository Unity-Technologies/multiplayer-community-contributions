using System.Collections;
using System.Collections.Generic;
using MLAPI.Transports.Tasks;
using UnityEngine;
using Photon.Realtime;

namespace MLAPI.Transports.PhotonRealtime
{
    public partial class PhotonRealtimeTransport : IMatchmakingCallbacks
    {
        /// <summary>
		/// Gets the current Master client of the current Room.
		/// </summary>
		/// <returns>The master client ID if the client in inside a Room, -1 otherwise.</returns>
		private int CurrentMasterId => this.m_Client.CurrentRoom != null ? this.m_Client.CurrentRoom.MasterClientId : -1;

        /// <summary>Photon ActorNumber of the host/server.</summary>
        private int m_originalRoomMasterClient = -1;

        public void OnCreatedRoom()
        {
        }

        public void OnCreateRoomFailed(short returnCode, string message)
        {
            m_ConnectTask = SocketTask.Fault;
            m_ConnectTask.Message = $"Create Room Failed: {message}";
            InvokeTransportEvent(NetworkEvent.Disconnect);
        }

        public void OnFriendListUpdate(List<FriendInfo> friendList)
        {
        }

        public void OnJoinedRoom()
        {
            Debug.LogFormat("Caching Original Master Client: {0}", CurrentMasterId);
            m_originalRoomMasterClient = CurrentMasterId;

            // Client connected to the room successfully, connection process is completed
            m_ConnectTask.IsDone = true;
            m_ConnectTask.Success = true;
            

            // any client (except host/server) need to know about their own join event
            if (!this.m_IsHostOrServer)
            {
                var senderId = GetMlapiClientId(m_Client.LocalPlayer.ActorNumber, false);
                
                NetworkEvent netEvent = NetworkEvent.Connect;
                InvokeTransportEvent(netEvent, senderId);
            }
        }

        public void OnJoinRandomFailed(short returnCode, string message)
        {
        }

        public void OnJoinRoomFailed(short returnCode, string message)
        {
            m_ConnectTask = SocketTask.Fault;
            m_ConnectTask.Message = $"Join Room Faileid: {message}";
            InvokeTransportEvent(NetworkEvent.Disconnect);
        }

        public void OnLeftRoom()
        {
            // any client (except host/server) need to know about their own leave event
            if (!this.m_IsHostOrServer)
            {
                var senderId = GetMlapiClientId(m_Client.LocalPlayer.ActorNumber, false);
                
                NetworkEvent netEvent = NetworkEvent.Connect;
                InvokeTransportEvent(netEvent, senderId);
            }
        }
    }
}