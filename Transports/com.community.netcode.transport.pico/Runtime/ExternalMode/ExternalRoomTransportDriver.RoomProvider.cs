using System;
using static Netcode.Transports.Pico.PicoTransport;

namespace Netcode.Transports.Pico
{
    public partial class ExternalRoomTransportDriver : IRoomProvider
    {
        private TransportRoomStatus _status = new TransportRoomStatus();

        private bool InitRoomProvider(TransportPicoRoomInfo picoRoomWrapper)
        {   
            _status.PicoRoomWrapper = picoRoomWrapper;
            _status.ParseUIDInfo();
            return true;
        }

        public bool IsSelfOwner()
        {
            return _status.SelfUID == _status.TransportRoomInfoOfUIDs.OwnerUID;
        }

        public bool IsInRoom()
        {
            return (0 != _status.SelfUID) && (0 != _status.TransportRoomInfoOfUIDs.RoomID);
        }

        public ulong GetSelfLoggedInUID()
        {
            return _status.SelfUID;
        }

        private TransportRoomInfo GetTransportPicoRoomInfo()
        {
            return _status.TransportRoomInfoOfUIDs;
        }
        
        private void HandleMsgFromRoom(string senderOpenID, byte[] msg)
        {
            ulong senderUID;
            if (!_status.OpenID2UIDs.TryGetValue(senderOpenID, out senderUID))
            {
                PicoTransportLog(LogLevel.Error, $"got msg from developer with unkown sender {senderOpenID}");
                return;
            }
            var payload = new ArraySegment<byte>(msg, 0, msg.Length);
            _picoTransport.OnPkgRecved(senderUID, payload);
        }

        public bool RoomKickUserByID(ulong roomID, ulong clientID)
        {
            string clientOpenID;
            if (!_status.UID2OpenIDs.TryGetValue(clientID, out clientOpenID))
            {
                PicoTransportLog(LogLevel.Error, $"RoomKickUserByID, {clientID} is not in this room");
                return false;
            }
            _status.PicoRoomWrapper.KickUser(roomID, clientOpenID);
            return true;
        }

        public bool RoomLeave(ulong roomID)
        {
            if (roomID != _status.TransportRoomInfoOfUIDs.RoomID)
            {
                PicoTransportLog(LogLevel.Error, $"RoomLeave, current is not in room {roomID}, skip this leave request");
                return false;
            }
            _status.PicoRoomWrapper.LeaveRoom(roomID);
            return true;
        }

        public bool SendPacket2UID(ulong clientID, byte[] dataArray)
        {
            string tgtOpenID;
            if (!_status.UID2OpenIDs.TryGetValue(clientID, out tgtOpenID))
            {
                PicoTransportLog(LogLevel.Error, $"SendPacket2UID, target({clientID}) is not in room, skip this send packet request");
                return false;
            }
            _status.PicoRoomWrapper.SendMsgToUID(tgtOpenID, dataArray);
            return true;
        }
    } //PicoMatchRoomProvider
}
