using UnityEngine;

using Pico.Platform.Models;
using Pico.Platform;
using System;
using static Netcode.Transports.Pico.PicoTransport;

using PicoPlatform = Pico.Platform;

namespace Netcode.Transports.Pico
{
    public partial class InnerPicoSDKUser
    {
        bool _isLastSessionMsg = true; //useful in editor
        private void RegisterNotificationCallbacks()
        {
            NetworkService.SetNotification_Game_ConnectionEventCallback(GameConnectionEventCallback);
            NetworkService.SetNotification_Game_Request_FailedCallback(RequestFailedCallback);
            NetworkService.SetNotification_Game_StateResetCallback(GameStateResetCallback);

            RoomService.SetLeaveNotificationCallback(RoomLeaveNotificationCallback);
            RoomService.SetJoin2NotificationCallback(RoomJoin2NotificationCallback);
            RoomService.SetKickUserNotificationCallback(RoomKickUserNotificationCallback);
            RoomService.SetUpdateOwnerNotificationCallback(RoomUpdateOwnerNotificationCallback);
            RoomService.SetUpdateNotificationCallback(RoomUpdateCallback);
        }

        private void GameConnectionEventCallback(PicoPlatform.Message<PicoPlatform.GameConnectionEvent> msg)
        {
            if (IsOldSessionCallback("connect_event"))
            {
                return;
            }
            var state = msg.Data;
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"OnGameConnectionEvent: {state}");
            if (state == PicoPlatform.GameConnectionEvent.Connected)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "GameConnection: success");
            }
            else if (state == PicoPlatform.GameConnectionEvent.Closed)
            {
                StopPicoGame("GameConnection: failed Please re-initialize");
            }
            else if (state == PicoPlatform.GameConnectionEvent.GameLogicError)
            {
                StopPicoGame("GameConnection: failed After successful reconnection, the logic state is found to be wrong, please re-initialize��");
            }
            else if (state == PicoPlatform.GameConnectionEvent.Lost)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "######, GameConnection: Reconnecting, waiting ...");
                if (0 == _restartTimeoutTick)
                {
                    /*
                        A single tick represents one hundred nanoseconds or one ten-millionth of a second. There are 10,000 ticks in a millisecond (see TicksPerMillisecond) and 10 million ticks in a second.                      
                     */
                    _restartTimeoutTick = DateTime.Now.Ticks + 10 * 1000 * 10000;
                }
            }
            else if (state == PicoPlatform.GameConnectionEvent.Resumed)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "######, GameConnection: successfully reconnected");
                _restartTimeoutTick = 0;
            }
            else if (state == PicoPlatform.GameConnectionEvent.KickedByRelogin)
            {
                StopPicoGame("GameConnection: login in other device, try reinitialize later");
            }
            else if (state == PicoPlatform.GameConnectionEvent.KickedByGameServer)
            {
                StopPicoGame("GameConnection: be kicked by server, try reinitialize later");
            }
            else
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "GameConnection: unknown error");
            }
        }

        private void RequestFailedCallback(PicoPlatform.Message<PicoPlatform.GameRequestFailedReason> msg)
        {
            if (IsOldSessionCallback("request_failed"))
            {
                return;
            }
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"OnRequestFailed: {msg.Data}");
        }

        private void GameStateResetCallback(PicoPlatform.Message msg)
        {
            if (IsOldSessionCallback("game_state_reset"))
            {
                return;
            }
            StopPicoGame("game state disaccord with that of the server, need re-initialization");
        }

        bool IsOldSessionCallback(string callbackDesc)
        {
            if (_isLastSessionMsg)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, $"ignore old session callback({callbackDesc})");
            }
            return _isLastSessionMsg;
        }

        private void RoomUpdateCallback(Message<Room> message)
        {
            if (IsOldSessionCallback("room_update"))
            {
                return;
            }
            CommonProcess("ProcessRoomUpdate", message, () =>
            {
                var room = message.Data;
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"!!!!!!, selfOpenID:{GetSelfOpenID()}, room update: {GameUtils.GetRoomLogData(room)}");
                _room_status.SetRoomInfo(GetSelfOpenID(), TransportPicoRoomInfo.GetPicoRoomInfo(room));
                OnPicoSdkNotify?.Invoke(ESDKUserEvent.RoomInfoUpdate, _curGameState.CurState, _curGameState.CurState, "room info updated");
            });
        }

        private void ProcessRoomJoin2(Message<Room> message)
        {
            CommonProcess("ProcessRoomJoin2", message, () =>
            {
                if (message.IsError)
                {
                    var err = message.Error;
                    PicoTransportLog(LogLevel.Error, $"Join room error {err.Message} code={err.Code}");
                    CallOnStatusChange(EGameState.NotInited, "Join room failed");
                    return;
                }
                var room = message.Data;
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"room {room.RoomId} join succeed: {GameUtils.GetRoomLogData(room)}");
                _room_status.SetRoomInfo(GetSelfOpenID(), TransportPicoRoomInfo.GetPicoRoomInfo(room));
                CallOnStatusChange(EGameState.InRoom, "in room now");
            });
        }

        private void ProcessRoomLeave(Message<Room> message)
        {
            CommonProcess("ProcessRoomLeave", message, () =>
            {
                if (message.IsError)
                {
                    var err = message.Error;
                    PicoTransportLog(LogLevel.Error, $"Leave room error {err.Message} code={err.Code}");
                    _room_status.SetRoomInfo(GetSelfOpenID(), null);
                    CallOnStatusChange(EGameState.Inited, "Leave room failed");
                    return;
                }
                var room = message.Data;
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"in ProcessRoomLeave, roomLeave: {GameUtils.GetRoomLogData(room)}");
                _room_status.SetRoomInfo(GetSelfOpenID(), TransportPicoRoomInfo.GetPicoRoomInfo(room));
                OnRoomLeft(room);
            });
            return;
        }

        private void OnRoomLeft(Room room)
        {
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"room {room.RoomId} leave succeed ...");
            if (GetRoomID() != room.RoomId)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"room {room.RoomId} leave succeed, but current is not in this room now, skip this request");
                return;
            }

            CallOnStatusChange(EGameState.Inited, "room left, return to 'inited'");
            _room_status.SetRoomInfo(GetSelfOpenID(), null);
        }

        private void RoomKickUserNotificationCallback(Message<Room> message)
        {
            if (IsOldSessionCallback("kick_user_notify"))
            {
                return;
            }
            CommonProcess("OnRoomKickUserNotification", message, () =>
            {
                var room = message.Data;
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, GameUtils.GetRoomLogData(room));
            });
        }

        private  void RoomUpdateOwnerNotificationCallback(Message message)
        {
            if (IsOldSessionCallback("update_owner_notify"))
            {
                return;
            }
            CommonProcess("OnRoomUpdateOwnerNotification", message, () =>
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "OnRoomUpdateOwnerNotification");
            });
        }

        private void RoomLeaveNotificationCallback(Message<Room> message)
        {
            if (IsOldSessionCallback("room_leave_notify"))
            {
                return;
            }
            CommonProcess("OnRoomLeaveNotification", message, () =>
            {
                var room = message.Data;
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, GameUtils.GetRoomLogData(room));
                OnRoomLeft(room);
            });
        }

        private void RoomJoin2NotificationCallback(Message<Room> message)
        {
            if (IsOldSessionCallback("join2_notify"))
            {
                return;
            }
            CommonProcess("OnRoomJoin2Notification", message, () =>
            {
                var room = message.Data;
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, GameUtils.GetRoomLogData(room));
            });
        }

        private void CommonProcess(string funName, Message message, Action action)
        {
            string errmsg = "";
            if (message.IsError)
            {
                errmsg = message.Error.Message;
            }
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"server msg({message.Type}:{funName}), err:{errmsg}");
            action();
        }
    }
}
