using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using UnityEngine;
using PicoPlatform = Pico.Platform;

namespace Netcode.Transports.Pico
{
    public class PicoRoomInfo
    {
        public ulong RoomID;
        public string OwnerOpenID;
        public readonly HashSet<string> CurRoomOpenIDs = new HashSet<string>();
    }

    public class TransportRoomInfo
    {
        public ulong RoomID;
        public ulong OwnerUID;
        public HashSet<ulong> RoomUIDs = new HashSet<ulong>();
    }

    public class TransportPicoRoomInfo
    {
        public enum ERoomEvent
        {
            TransportShutdown = 1,
            OwnerLeaveRoom = 2,
            SelfLeaveRoom = 3,
            UpdateRoomInfo = 4
        }
        public string SelfOpenID;
        public PicoRoomInfo PicoRoomInfo;
        public event Action<ERoomEvent, string, PicoRoomInfo> OnRoomEvent;

        public void InitWithPicoRoomInfo(string selfOpenID, PicoRoomInfo roomInfo)
        {
            SelfOpenID = selfOpenID;
            PicoRoomInfo = roomInfo;
        }

        public int SendMsgToUID(string tgtOpenID, byte[] message)
        {
            PicoPlatform.NetworkService.SendPacket(tgtOpenID, message, true);
            return 0;
        }

        public int KickUser(ulong roomID, string userOpenId)
        {
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Warn, $"PicoKickUser, roomID: {roomID}, userOpenId:{userOpenId}");
            PicoPlatform.RoomService.KickUser(roomID, userOpenId, -1).OnComplete(HandleKickPlayerResponse);
            return 0;
        }

        public int LeaveRoom(ulong roomID)
        {
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"PicoLeaveRoom, roomID: {roomID}");
            PicoPlatform.RoomService.Leave(roomID).OnComplete(HandleLeaveRoomResponse);
            return 0;
        }

        void CommonProcess(string funName, PicoPlatform.Message message, Action action)
        {
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"message.Type: {message.Type}");
            if (!message.IsError)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"{funName} no error");
                action();
            }
            else
            {
                var error = message.Error;
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"{funName} error: {error.Message}");
            }
        }

        void HandleLeaveRoomResponse(PicoPlatform.Message<PicoPlatform.Models.Room> message)
        {
            CommonProcess("HandleLeaveRoom", message, () =>
            {
                var result = message.Data;
                RoomInfoUpdate(result);
            });
        }

        void HandleKickPlayerResponse(PicoPlatform.Message<PicoPlatform.Models.Room> message)
        {
            CommonProcess("HandleKickPlayerResponse", message, () =>
            {
                var result = message.Data;
                RoomInfoUpdate(result);
            });
        }

        static public PicoRoomInfo GetPicoRoomInfo(PicoPlatform.Models.Room room)
        {
            PicoRoomInfo roomInfo = new PicoRoomInfo();
            roomInfo.RoomID = room.RoomId;
            if (room.OwnerOptional != null)
            {
                roomInfo.OwnerOpenID = room.OwnerOptional.ID;
            }
            else
            {
                roomInfo.OwnerOpenID = "";
            }
            if (room.UsersOptional != null)
            {
                foreach (PicoPlatform.Models.User user in room.UsersOptional)
                {
                    roomInfo.CurRoomOpenIDs.Add(user.ID);
                }
            }
            else
            {
                roomInfo.CurRoomOpenIDs.Clear();
            }
            return roomInfo;
        }

        private void RoomInfoUpdate(PicoPlatform.Models.Room roomInfo)
        {
            if (roomInfo.RoomId == 0)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "RoomInfoUpdate, room_id is 0(leave room notification)");
                OnRoomEvent?.Invoke(ERoomEvent.SelfLeaveRoom, "room info update with room_id == 0", null);
                return;
            }
            PicoRoomInfo = GetPicoRoomInfo(roomInfo);
            OnRoomEvent?.Invoke(ERoomEvent.UpdateRoomInfo, "room info update", PicoRoomInfo);
        }
    }

    public class TransportRoomStatus
    {
        public ulong SelfUID;
        public TransportPicoRoomInfo PicoRoomWrapper = null;
        public TransportRoomInfo TransportRoomInfoOfUIDs = new TransportRoomInfo();
        public Dictionary<string, ulong> OpenID2UIDs = new Dictionary<string, ulong>();
        public Dictionary<ulong, string> UID2OpenIDs = new Dictionary<ulong, string>();

        public void SetRoomInfo(string selfOpenID, PicoRoomInfo roomInfo)
        {
            if (null == PicoRoomWrapper)
            {
                PicoRoomWrapper = new TransportPicoRoomInfo();
                PicoRoomWrapper.InitWithPicoRoomInfo(selfOpenID, roomInfo);
            }
            PicoRoomWrapper.PicoRoomInfo = roomInfo;
            ParseUIDInfo();
        }

        public void ParseUIDInfo()
        {
            if (null == PicoRoomWrapper || null == PicoRoomWrapper.PicoRoomInfo || PicoRoomWrapper.PicoRoomInfo.OwnerOpenID.Length<=0)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "null roomInfo, maybe leave room, clear TransportRoomInfoOfUIDs");
                TransportRoomInfoOfUIDs.RoomID = 0;
                TransportRoomInfoOfUIDs.OwnerUID = 0;
                TransportRoomInfoOfUIDs.RoomUIDs.Clear();
                return;
            }
            TransportRoomInfoOfUIDs.RoomID = PicoRoomWrapper.PicoRoomInfo.RoomID;
            SelfUID = (ulong)(PicoRoomWrapper.SelfOpenID.GetHashCode());
            TransportRoomInfoOfUIDs.OwnerUID = (ulong)(PicoRoomWrapper.PicoRoomInfo.OwnerOpenID.GetHashCode());
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"ParseUIDInfo: roomID {TransportRoomInfoOfUIDs.RoomID}, ownerUID {TransportRoomInfoOfUIDs.OwnerUID}, selfUID {SelfUID} from selfOpenID {PicoRoomWrapper.SelfOpenID}");

            TransportRoomInfoOfUIDs.RoomUIDs.Clear();
            OpenID2UIDs.Clear();
            UID2OpenIDs.Clear();
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"roomID {TransportRoomInfoOfUIDs.RoomID},  <uid, openid> mapping ...");
            foreach (string playerName in PicoRoomWrapper.PicoRoomInfo.CurRoomOpenIDs)
            {
                ulong hashCode = (ulong)playerName.GetHashCode();
                TransportRoomInfoOfUIDs.RoomUIDs.Add(hashCode);
                OpenID2UIDs.Add(playerName, hashCode);
                UID2OpenIDs.Add(hashCode, playerName);
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"uid({hashCode})<->openID({playerName})");
            }
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "... uid openid mapping");
        }
    } //TransportRoomStatus

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void DebugDelegate(int level, IntPtr strPtr);

    public interface IRoomEventHandler
    {
        void OnRoomInfoUpdate(TransportRoomInfo transportRoomInfo);
        void OnPkgRecved(ulong senderUID, ArraySegment<byte> pkg);
    }

    public interface IRoomProvider
    {
        bool IsSelfOwner();
        ulong GetSelfLoggedInUID();
        bool RoomKickUserByID(ulong roomID, ulong clientId);
        bool RoomLeave(ulong roomID);
        bool SendPacket2UID(ulong clientId, byte[] dataArray);
    }

}
