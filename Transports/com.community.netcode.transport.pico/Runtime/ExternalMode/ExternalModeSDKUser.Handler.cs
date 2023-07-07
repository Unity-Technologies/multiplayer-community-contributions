using UnityEngine;

using Pico.Platform.Models;
using Pico.Platform;
using System;
using static Netcode.Transports.Pico.PicoTransport;

namespace Netcode.Transports.Pico
{
    public partial class ExternalModeSDKUser
    {
        bool _isLastSessionMsg = true;
        long _restartTimeoutTick = 0;

        private void Update()
        {
            if (EGameState.InRoom == _curGameState.CurState)
            {
                if (_transportDriver != null)
                {
                    _transportDriver.Update();
                }
            }
            if (_restartTimeoutTick > 0)
            {
                long curTick = DateTime.Now.Ticks;
                if (curTick > _restartTimeoutTick)
                {
                    _restartTimeoutTick = 0;
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, "_restartTimeoutTick met, stop PicoSDKUser");
                    StopNetcode("restart timeout");
                }
            }
        }

        private void RegisterNotificationCallbacks()
        {
            NetworkService.SetNotification_Game_ConnectionEventCallback(GameConnectionEventCallback);
            NetworkService.SetNotification_Game_Request_FailedCallback(RequestFailedCallback);
            NetworkService.SetNotification_Game_StateResetCallback(GameStateResetCallback);

            MatchmakingService.SetMatchFoundNotificationCallback(MatchmakingMatchFoundCallback);
            MatchmakingService.SetCancel2NotificationCallback(MatchmakingCancel2NotificationCallback);

            RoomService.SetLeaveNotificationCallback(RoomLeaveNotificationCallback);
            RoomService.SetJoin2NotificationCallback(RoomJoin2NotificationCallback);
            RoomService.SetKickUserNotificationCallback(RoomKickUserNotificationCallback);
            RoomService.SetUpdateOwnerNotificationCallback(RoomUpdateOwnerNotificationCallback);
            RoomService.SetUpdateNotificationCallback(RoomUpdateCallback);
        }

        bool IsOldSessionCallback(string callbackDesc)
        {
            if (_isLastSessionMsg)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, $"ignore old session callback({callbackDesc})");
            }
            return _isLastSessionMsg;
        }

        private void MatchmakingMatchFoundCallback(Message<Room> message)
        {
            if (IsOldSessionCallback("matchmaking_found"))
            {
                return;
            }
            CommonProcess("ProcessMatchmakingMatchFound", message, () =>
            {
                var room = message.Data;
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"in ProcessMatchmakingMatchFound, roomInfo: {GameUtils.GetRoomLogData(room)}");
                _curGameState.RoomID = room.RoomId;
                if (_curGameState.RoomID == 0)
                {
                    CallOnStatusChange(EGameState.NotInited, "matchmaking failed");
                    return;
                }
                _curGameState.RoomData = room;
                CallOnStatusChange(EGameState.MatchFound, "matchmaking succeed");
            });

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
                _curGameState.RoomData = room;
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"!!!!!!, got room update notification: {GameUtils.GetRoomLogData(room)}");
                if (_transportDriver != null)
                {
                    _transportDriver.DriverRoomInfoUpdate(TransportPicoRoomInfo.GetPicoRoomInfo(room));
                }
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
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"in ProcessRoomJoin2, roomData: {GameUtils.GetRoomLogData(room)}");
                OnRoomJoined(room);
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
                    CallOnStatusChange(EGameState.NotInited, "Leave room failed");
                    return;
                }
                var room = message.Data;
                PicoTransportLog(LogLevel.Info, $"in ProcessRoomLeave, roomLeave: {GameUtils.GetRoomLogData(room)}");
                
                OnRoomLeft(room);
            });
            return;
        }

        private void HandleDriverEvent(ExternalRoomTransportDriver.ETransportDriverEvent type, int errorCode, string errorInfo)
        {
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"HandleDriverEvent, event {type}");
            switch (type)
            {
                case ExternalRoomTransportDriver.ETransportDriverEvent.BeforeReenter:
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"transport driver event [before reenter...]");
                    return;
                case ExternalRoomTransportDriver.ETransportDriverEvent.AfterReenter:
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"transport driver event [... after reenter]");
                    return;
                case ExternalRoomTransportDriver.ETransportDriverEvent.Stopped:
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"transport driver stopped: ({errorCode}, {errorInfo}), stop transport driver now");
                    break;
                default:
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"error: ({errorCode}, {errorInfo}), stop transport driver now");
                    break;
            }
            if (_transportDriver != null)
            {
                _transportDriver.OnDriverEvent -= HandleDriverEvent;
                _transportDriver.OnClientEvent -= HandleClientEvent;
                _transportDriver = null;
            }
            CallOnStatusChange(EGameState.NotInited, "pico transport shutdown, return to state NotInited");
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"driver uninited, return to start scene ...");
        }

        private void HandleClientEvent(Unity.Netcode.NetworkEvent type, ulong clientID)
        {
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"!!!, event from transport driver: (event:{type}, clientID:{clientID})");
            if (type == Unity.Netcode.NetworkEvent.Connect)
            {
                if (Unity.Netcode.NetworkManager.Singleton.IsServer && (ServerCreatePlayerPrefabPath.Length > 0))
                {
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"!!!, spawn player, clientID:{clientID})");
                    UnityEngine.Object prefab = Resources.Load(ServerCreatePlayerPrefabPath);
                    GameObject go = (GameObject)Instantiate(prefab, Vector3.zero, Quaternion.identity);
                    go.GetComponent<Unity.Netcode.NetworkObject>().SpawnWithOwnership(clientID, true);
                }
            } else
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"!!!, TODO: dicsonnect event from transport driver: (event:{type}, clientID:{clientID})");
            }
        }

        private void OnRoomJoined(Room room)
        {
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"room {room.RoomId} join succeed ...");
            _curGameState.RoomID = room.RoomId;
            _curGameState.RoomData = room;
            CallOnStatusChange(EGameState.InRoom, "in room now");
        }

        private void OnRoomLeft(Room room)
        {
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"room {room.RoomId} leave succeed, leave fighting scene ...");
            if (_curGameState.RoomID != room.RoomId)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"room {room.RoomId} leave succeed, but current is not in this room now, skip this request");
                return;
            }

            _curGameState.LastRoomID = _curGameState.RoomID;
            _curGameState.RoomID = 0;
            _curGameState.RoomData = null;
            CallOnStatusChange(EGameState.RoomLeft, "room left");
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

        private void MatchmakingCancel2NotificationCallback(Message message)
        {
            if (IsOldSessionCallback("match_cancel"))
            {
                return;
            }
            CommonProcess("OnMatchmakingCancel2Notification", message, () =>
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "OnMatchmakingCancel2Notification");
            });
        }
        private void RoomLeaveNotificationCallback(Message<Room> message)
        {
            if (IsOldSessionCallback("leave_notify"))
            {
                return;
            }
            CommonProcess("OnRoomLeaveNotification", message, () =>
            {
                var room = message.Data;
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "room leave notification:"+GameUtils.GetRoomLogData(room));
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
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "room join2 response:"+GameUtils.GetRoomLogData(room));
            });
        }

        private void CommonProcess(string funName, Message message, Action action)
        {
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"message.Type: {message.Type}");
            if (!message.IsError)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"{funName} no error");
            }
            else
            {
                var error = message.Error;
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, $"{funName} error: {error.Message}");
            }
            action();
        }

        private void ProcessMatchmakingEnqueue(Message<MatchmakingEnqueueResult> message)
        {
            CommonProcess("ProcessMatchmakingEnqueue", message, () =>
            {
                if (message.IsError)
                {
                    EGameState newState = _curGameState.CurState >= EGameState.Inited ? EGameState.Inited : EGameState.NotInited;
                    CallOnStatusChange(newState, "matchmaking enqueue failed");
                    return;
                }
                var result = message.Data;
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, GameUtils.GetMatchmakingEnqueueResultLogData(result));
                CallOnStatusChange(EGameState.InMatching, "got matchmaking response\nwaiting other player ...");
            });
        }

        private void ProcessCreatePrivate(Message<Room> message)
        {
            CommonProcess("ProcessCreatePrivate", message, () =>
            {
                if (message.IsError)
                {
                    EGameState newState = _curGameState.CurState >= EGameState.Inited ? EGameState.Inited : EGameState.NotInited;
                    CallOnStatusChange(newState, "create private room failed");
                    return;
                }
                _curGameState.RoomData = message.Data;
                _curGameState.RoomID = _curGameState.RoomData.RoomId;
                if (_curGameState.RoomID == 0)
                {
                    PicoTransportLog(LogLevel.Error, "unexpected RoomID 0");
                    CallOnStatusChange(EGameState.NotInited, "unexpected RoomID 0");
                    return;
                }
                PicoTransportLog(LogLevel.Info, "got create private response:" + GameUtils.GetRoomLogData(_curGameState.RoomData));
                CallOnStatusChange(EGameState.InRoom, "got create private response");
            });
        }

    }
}
