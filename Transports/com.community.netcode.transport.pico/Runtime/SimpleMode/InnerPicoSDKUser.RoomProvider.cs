using System;
using UnityEngine;
using static Netcode.Transports.Pico.PicoTransport;
using PicoPlatform = Pico.Platform;

namespace Netcode.Transports.Pico
{
    public partial class InnerPicoSDKUser : IRoomProvider
    {
        private TransportRoomStatus _room_status = new TransportRoomStatus();

       public bool IsSelfOwner()
        {
            return _room_status.SelfUID == _room_status.TransportRoomInfoOfUIDs.OwnerUID;
        }

        public ulong GetSelfLoggedInUID()
        {
            return _room_status.SelfUID;
        }

        public bool RoomKickUserByID(ulong roomID, ulong clientId)
        {
            return false;
        }

        public bool RoomLeave(ulong roomID)
        {
            _room_status.PicoRoomWrapper.LeaveRoom(roomID);
            return true;
        }

        public bool SendPacket2UID(ulong clientID, byte[] dataArray)
        {
            string tgtOpenID;
            if (!_room_status.UID2OpenIDs.TryGetValue(clientID, out tgtOpenID))
            {
                PicoTransportLog(LogLevel.Error, $"SendPacket2UID, target({clientID}) is not in room, skip this send packet request");
                return false;
            }
            _room_status.PicoRoomWrapper.SendMsgToUID(tgtOpenID, dataArray);
            return false;
        }
    }
}
