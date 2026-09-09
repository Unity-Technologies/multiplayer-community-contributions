using System;
using System.Collections;
using UnityEngine;
using static Netcode.Transports.Pico.PicoTransport;
using PicoPlatform = Pico.Platform;

namespace Netcode.Transports.Pico
{
    [RequireComponent(typeof(PicoTransport))]
    public partial class InnerPicoSDKUser : MonoBehaviour
    {
        public enum ESDKUserEvent
        {
            StateChange = 1,
            RoomInfoUpdate = 2,
            Error = 3,
        }

        public enum EGameState
        {
            NotInited,
            InIniting,
            UserGot,
            InGameIniting,
            Inited,
            Shutdowned,
            RoomJoining,
            InRoom,
            RoomLeaving,
        }
        private struct CurGameState
        {
            public EGameState CurState;
            public ulong LastRoomID;
            public PicoPlatform.Models.User UserInfo;
        }

        public class DevInitInfo
        {
            public string host;
            public ushort port;
            public string app_id;
            public int region_id;
            public bool via_proxy;
            public string fakeToken;
            public string openidOfFakeToken;
        }

        //params: event, oldState, curState, eventDesc
        public event Action<ESDKUserEvent, EGameState, EGameState, string> OnPicoSdkNotify;
        //params: senderUID, message
        public event Action<ulong, ArraySegment<byte>> OnPicoRoomMessage;

        private CurGameState _curGameState;
        private long _restartTimeoutTick = 0;

        // Start is called before the first frame update
        private void Start()
        {
            RegisterNotificationCallbacks();
        }

        private void OnEnable()
        {
            _curGameState.CurState = EGameState.NotInited;
            _curGameState.LastRoomID = 0;
            _curGameState.UserInfo = null;
        }

        private void OnDestroy()
        {
            StopPicoGame("InnerSDKUser destroyed");
        }

        private void Update()
        {
            if (_curGameState.CurState == EGameState.InRoom)
            {
                RecvRoomPackage();
            }
            if (_restartTimeoutTick > 0)
            {
                long curTick = DateTime.Now.Ticks;
                if (curTick > _restartTimeoutTick)
                {
                    _restartTimeoutTick = 0;
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, "_restartTimeoutTick met, stop PicoSDKUser");
                    StopPicoGame("restart timeout");
                }
            }
        }

        public void HandlePicoTransportEvent(PicoTransport.ETransportEvent transportState)
        {
            switch (transportState)
            {
                case PicoTransport.ETransportEvent.Stopped:
                    {
                        if (_curGameState.CurState >= EGameState.Inited)
                        {
                            CallOnStatusChange(EGameState.NotInited, "transport stopped, return to state 'inited'");
                        }
                    }
                    break;
                default:
                    break;
            }
            return;
        }

        private void RecvRoomPackage()
        {
            if (_curGameState.CurState == EGameState.InRoom)
            {
                // read packet
                var packet = PicoPlatform.NetworkService.ReadPacket();
                while (packet != null)
                {
                    HandleMsgFromRoom(packet);
                    packet.Dispose();
                    packet = PicoPlatform.NetworkService.ReadPacket();
                }
            }
        }

        private void HandleMsgFromRoom(PicoPlatform.Models.Packet pkgFromRoom)
        {
            ulong senderUID;
            if (!_room_status.OpenID2UIDs.TryGetValue(pkgFromRoom.SenderId, out senderUID))
            {
                PicoTransportLog(LogLevel.Error, $"got msg from developer with unkown sender open_id: {pkgFromRoom.SenderId}, uid: {senderUID}");
                return;
            }
            byte[] message = new byte[pkgFromRoom.Size];
            ulong pkgSize = pkgFromRoom.GetBytes(message);
            if (pkgSize <= 0)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, $"HandleMsgFromRoom, error pkgSize: {pkgSize}");
                OnPicoSdkNotify?.Invoke(ESDKUserEvent.Error, _curGameState.CurState, _curGameState.CurState, "error on recved msg");
                return;
            }
            var payload = new ArraySegment<byte>(message, 0, (int)pkgSize);
            OnPicoRoomMessage?.Invoke(senderUID, payload);
        }

        public string GetSelfOpenID()
        {
            if (null == _curGameState.UserInfo)
            {
                return "not inited user";
            }
            return _curGameState.UserInfo.ID;
        }

        public TransportRoomInfo GetTransportRoomInfo()
        {
            if (0 == GetRoomID())
            {
                return null;
            }
            return _room_status.TransportRoomInfoOfUIDs;
        }

        private void CallOnStatusChange(EGameState curState, string statusDesc)
        {
            PicoTransportLog(LogLevel.Info, $"{_curGameState.CurState} > {curState}, {statusDesc}");
            var lastState = _curGameState.CurState;
            _curGameState.CurState = curState;
            if (curState == EGameState.InRoom)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"new enter room, save room_id to LastRoomID ({GetRoomID()})");
                _curGameState.LastRoomID = GetRoomID();
            }
            OnPicoSdkNotify?.Invoke(ESDKUserEvent.StateChange, lastState, curState, statusDesc);
        }

        private IEnumerator InitPicoPlatformAndGame(TextAsset mock_config)
        {
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"InitPicoPlatformAndGame be called");
            _isLastSessionMsg = true;
            _restartTimeoutTick = 0;
            yield return null;
            if (_curGameState.CurState >= EGameState.Inited)
            {
                _isLastSessionMsg = false;
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Warn, $"already inited, Change to GameState.Inited directly");
                CallOnStatusChange(EGameState.Inited, "game has already inited");
                yield break;
            }
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"InitPicoPlatformAndGame start coreservice init ...");
            try
            {
                PicoPlatform.CoreService.Initialize(null);
            }
            catch (Exception e)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"exception init pico Core {e}");
            }
            if (!PicoPlatform.CoreService.Initialized)
            {
                CallOnStatusChange(EGameState.NotInited, "pico SDK init failed ...");
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, "pico initialize failed");
                yield break;
            }
            CallOnStatusChange(_curGameState.CurState, "CoreService get access token started ...");
            PicoPlatform.UserService.GetAccessToken().OnComplete(delegate (PicoPlatform.Message<string> message)
            {
                if (message.IsError)
                {
                    var err = message.Error;
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, $"Got access token error {err.Message} code={err.Code}");
                    CallOnStatusChange(EGameState.NotInited, "CoreService get access token failed");

                    return;
                }
                string accessToken = message.Data;
                CallOnStatusChange(_curGameState.CurState, $"get access token succeed: {accessToken}");
                PicoPlatform.UserService.GetLoggedInUser().OnComplete(delegate (PicoPlatform.Message<PicoPlatform.Models.User> userInfo)
                {
                    if (userInfo.IsError)
                    {
                        var err = userInfo.Error;
                        PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, $"GetLoggedInUser error {err.Message} code={err.Code}");
                        CallOnStatusChange(EGameState.NotInited, "UserService.GetLoggedInUser failed");
                        return;
                    }

                    if (userInfo.Data == null)
                    {
                        CallOnStatusChange(EGameState.NotInited, "UserService.GetLoggedInUser userInfo.Data is nil");
                        return;
                    }
                    _curGameState.UserInfo = userInfo.Data;
                    if (userInfo.Data.ID == "unknow_openid")
                    {
                        CallOnStatusChange(EGameState.NotInited, "UserService.GetLoggedInUser invalid openid in User.Data");
                        return;
                    }
                    CallOnStatusChange(EGameState.UserGot, $"GetLoggedInUser complete, player openID: {userInfo.Data.ID}");

                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"got accessToken {accessToken}, GameInitialize begin");
                    PicoPlatform.CoreService.GameUninitialize();
                    PicoPlatform.Task<PicoPlatform.GameInitializeResult> request;
                    CallOnStatusChange(EGameState.InGameIniting, "Start game initialization, player openID");
                    _isLastSessionMsg = false;
                    request = PicoPlatform.CoreService.GameInitialize(accessToken);
                    CallOnStatusChange(_curGameState.CurState, "game init started, requestID:" + request.TaskId);
                    if (request.TaskId != 0)
                    {
                        request.OnComplete(OnGameInitialize);
                    }
                    else
                    {
                        PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"Core.GameInitialize requestID is 0! Repeated initialization or network error");
                        CallOnStatusChange(EGameState.NotInited, "game init start failed!");
                    }
                });
            });
        }

        public void StartPicoGame(TextAsset mock_config = null)
        {
            if (_curGameState.CurState < EGameState.Inited)
            {
                CallOnStatusChange(EGameState.InIniting, "StartPicoGame, pico SDK init start ...");                
                StartCoroutine(InitPicoPlatformAndGame(mock_config));
            } else if (_curGameState.CurState == EGameState.Inited)
            {
                CallOnStatusChange(EGameState.InIniting, "StartPicoGame, already inited, sim InInit > Inited procedure, switch to InIniting ...");
                CallOnStatusChange(EGameState.Inited, "StartPicoGame, already inited, sim InInit > Inited, switch to Inited ...");
            }
            else
            {
                CallOnStatusChange(_curGameState.CurState, "StartPicoGame in Inited State, skip request");
            }
        }

        public void StartJoinNamedRoom(string roomName, string password, bool createIfNotExist)
        {
            CallOnStatusChange(EGameState.RoomJoining, $"request to join named room {roomName} ...");
            var roomOptions = GameUtils.GetRoomOptions(GetRoomID().ToString(), null, null, null, null, null);
            roomOptions.SetRoomName(roomName);
            if (password.Length > 0)
            {
                roomOptions.SetPassword(password);
            }
            PicoPlatform.RoomService.JoinOrCreateNamedRoom(PicoPlatform.RoomJoinPolicy.Everyone, createIfNotExist, 2, roomOptions).OnComplete(ProcessRoomJoin2);
        }

        public void StartLeaveRoom()
        {
            if (GetRoomID() == 0)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, "no valid room to leave");
                return;
            }
            if (_curGameState.CurState < EGameState.RoomJoining)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, "not InRoom, skip this request");
                return;
            }
            ulong room_id = GetRoomID();
            CallOnStatusChange(EGameState.RoomLeaving, $"request to Leaving room {GetRoomID()} ...");
            PicoPlatform.RoomService.Leave(room_id).OnComplete(ProcessRoomLeave);
        }

        public ulong GetRoomID()
        {
            if (_curGameState.CurState != EGameState.InRoom && _curGameState.CurState != EGameState.RoomLeaving)
            {
                //PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Warn, $"GetRoomID, current not in room, CurState {_curGameState.CurState}");
                return 0;
            }
            return _room_status.PicoRoomWrapper.PicoRoomInfo.RoomID;
        }

        public ulong GetPreviousRoomID()
        {
            return _curGameState.LastRoomID;
        }

        private void StopPicoGame(string errormsg)
        {
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"StopPicoGame, reason: {errormsg} ...");
            PicoPlatform.CoreService.GameUninitialize();
            if (_curGameState.CurState >= EGameState.Inited)
            {
                CallOnStatusChange(EGameState.NotInited, errormsg);//game module has been inited
            } else
            {
                CallOnStatusChange(EGameState.NotInited, errormsg);//game module has not been inited
            }
        }

        private void OnGameInitialize(PicoPlatform.Message<PicoPlatform.GameInitializeResult> msg)
        {
            if (msg == null)
            {
                StopPicoGame("OnGameInitialize: fail, message is null");
                return;
            }

            if (msg.IsError)
            {
                StopPicoGame($"GameInitialize Failed: {msg.Error.Code}, {msg.Error.Message}");
            }
            else
            {
                bool is_suc = msg.Data == PicoPlatform.GameInitializeResult.Success;
                if (is_suc)
                {
                    CallOnStatusChange(EGameState.Inited, "game init succeed");
                } else
                {
                    StopPicoGame($"GameInitialize: failed please re-initialize, info: {(PicoPlatform.GameInitializeResult)msg.Data}");
                }
            }
        }
    } // public partial class InnerPicoSDKUser
} // namespace Netcode.Transports.Pico

