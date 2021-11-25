using Photon.Realtime;
using Unity.Netcode;

namespace Netcode.Transports.PhotonRealtime
{
    public partial class PhotonRealtimeTransport : IInRoomCallbacks
    {
        public void OnMasterClientSwitched(Player newMasterClient)
        {
        }

        /// <summary>
        /// Called when a remote player entered the room. This Player is already added to the playerlist.
        /// </summary>
        /// <remarks>See base for remarks.</remarks>
        public void OnPlayerEnteredRoom(Player newPlayer)
        {
            // server/host gets join events (all others don't need this)

            if (m_IsHostOrServer)
            {
                
                var senderId = GetMlapiClientId(newPlayer.ActorNumber, false);
                //Debug.Log("Host got OnPlayerEnteredRoom() with senderId: "+senderId);

                NetworkEvent netEvent = NetworkEvent.Connect;
                InvokeTransportEvent(netEvent, senderId);
            }
        }

        /// <summary>
        /// Called when a remote player left the room or became inactive. Check otherPlayer.IsInactive.
        /// </summary>
        /// <remarks>See base for remarks.</remarks>
        public void OnPlayerLeftRoom(Player otherPlayer)
        {
            // server/host gets any player's leave. 
            // all clients disconnect when the server/host leaves.

            if (m_IsHostOrServer)
            {
                var senderId = GetMlapiClientId(otherPlayer.ActorNumber, false);
                //Debug.Log("Host got OnPlayerLeftRoom() with senderId: "+senderId);
                
                NetworkEvent netEvent = NetworkEvent.Disconnect;
                InvokeTransportEvent(netEvent, senderId);
            }
            else if (otherPlayer.ActorNumber == m_originalRoomMasterClient)
            {
                NetworkEvent netEvent = NetworkEvent.Disconnect;
                InvokeTransportEvent(netEvent, GetMlapiClientId(m_originalRoomMasterClient, false));
            }
        }

        public void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
        {
        }

        public void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
        {
        }
    }
}