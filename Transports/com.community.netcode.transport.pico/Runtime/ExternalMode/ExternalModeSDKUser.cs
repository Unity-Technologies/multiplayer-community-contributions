using System;
using System.Collections;
using Pico.Platform.Models;
using Pico.Platform;
using UnityEngine;

namespace Netcode.Transports.Pico
{
    [RequireComponent(typeof(PicoTransport))]
    public partial class ExternalModeSDKUser : MonoBehaviour
    {
        public string ServerCreatePlayerPrefabPath;

        public enum EGameState
        {
            NotInited,
            InIniting,
            UserGot,
            InGameIniting,
            Inited,
            InMatching,
            MatchFound,
            RoomLeaving,
            RoomLeft,
            RoomJoining,
            InRoom,            
        }
        private struct CurGameState
        {
            public EGameState CurState;
            public ulong LastRoomID;
            public ulong RoomID;
            public User UserInfo;
            public Room RoomData;
        }

        public class DevInitInfo
        {
            public string host;
            public ushort port;
            public string fakeAccessToken;
            public string selfOpenID;
            public string app_id;
            public int region_id;
            public bool via_proxy;
        }   
        
        //params: oldState, curState, statusDesc, selfOpenID, roomDesc
        public event Action<EGameState, EGameState, string, string, string> OnStatusChange;

        private CurGameState _curGameState;
        private ExternalRoomTransportDriver _transportDriver;
        private bool _autoRestartNetcodeOnHostLeave;
        private bool _autoMatchmaking = true;
        private string _matchPoolName = "test_pool_basic_2";

        // Start is called before the first frame update
        private void Start()
        {
            _curGameState.CurState = EGameState.NotInited;
            _curGameState.RoomID = 0;
            RegisterNotificationCallbacks();
        }

        private void OnDestroy()
        {
            StopNetcode("SimpleSDKUser destroyed");
        }
 
        public void SetAutoMatchmakingAfterInit(bool autoMatchmaking, string poolName = "")
        {
            _autoMatchmaking = autoMatchmaking;
            _matchPoolName = poolName.Length > 0 ? poolName : _matchPoolName;
        }

        public void StartPicoGame(TextAsset mock_config = null)
        {
            if (_curGameState.CurState < EGameState.Inited)
            {
                CallOnStatusChange(EGameState.InIniting, "pico SDK init started ...");
                StartCoroutine(InitPicoPlatformAndGame(mock_config));
            }
            else
            {
                CallOnStatusChange(_curGameState.CurState, "StartPicoGame in Inited State, skip request");
                if (_autoMatchmaking)
                {
                    //already Inited, start Matchmaking immediately
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"auto start matchmaking in StartPicoGame ...");
                    StartMatchmaking();
                }
            }
        }

        private void EndPicoGame(string reason)
        {
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"StopPicoGame, reason: {reason} ...");
            CoreService.GameUninitialize();
            _curGameState.LastRoomID = 0;
            CallOnStatusChange(EGameState.NotInited, reason);
        }


        public void StartNetcode()
        {
            if (null == _transportDriver)
            {
                _transportDriver = new ExternalRoomTransportDriver();
                _transportDriver.OnClientEvent += HandleClientEvent;
                _transportDriver.OnDriverEvent += HandleDriverEvent;
            }
            string selfOpenID = _curGameState.UserInfo.ID;
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"StartPicoSDKUser, selfOpenID is {selfOpenID}");
            _restartTimeoutTick = 0;
            bool result = _transportDriver.Init( _autoRestartNetcodeOnHostLeave, GetComponent<PicoTransport>(), selfOpenID, _curGameState.RoomData);
            if (!result)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Warn, "init pico driver failed");
            }
        }

        public void StopNetcode(string desc)
        {
            if (null == _transportDriver)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"StopPicoSDKUser, transport driver is nil, skip driver uninit, this stop reason({desc})");
            }
            else
            {
                _transportDriver.Uninit($"stop pico sdk user(reason:{desc})");
                _transportDriver = null;
            }
            EndPicoGame($"stop pico sdk user(reason:{desc}");
        }

        public void StartMatchmaking()
        {
            CallOnStatusChange(EGameState.InMatching, "start matchmaking ...");
            MatchmakingService.Enqueue2(_matchPoolName, null).OnComplete(ProcessMatchmakingEnqueue);            
        }

        public void StartCreatePrivateRoom(bool everyone)
        {
            CallOnStatusChange(EGameState.InMatching, "start create private ...");
            RoomJoinPolicy joinPolicy = RoomJoinPolicy.None;
            if (everyone)
            {
                joinPolicy = RoomJoinPolicy.Everyone;
            }
            RoomOptions roomOption = new RoomOptions();
            RoomService.CreateAndJoinPrivate2(joinPolicy, 2, roomOption).OnComplete(ProcessCreatePrivate);
        }

        public void StartJoinRoomByRoomID(ulong roomID)
        {
            _curGameState.RoomID = roomID;
            StartJoinRoom();
            return;
        }

        public void StartJoinNamedRoom(string roomName, string password)
        {
            if (_curGameState.CurState < EGameState.Inited)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, $"StartJoinRoom, but game state {_curGameState.CurState} is invalid");
                return;
            }
            CallOnStatusChange(EGameState.RoomJoining, $"request to join named room {roomName} ...");
            var roomOptions = GameUtils.GetRoomOptions(_curGameState.RoomID.ToString(), null, null, null, null, null);
            roomOptions.SetRoomName(roomName);
            if (password.Length > 0)
            {
                roomOptions.SetPassword(password);
            }
            RoomService.JoinOrCreateNamedRoom(RoomJoinPolicy.Everyone, true, 2, roomOptions).OnComplete(ProcessRoomJoin2);
        }

        public void StartJoinRoom()
        {   
            if (_curGameState.RoomID == 0)
            {
                if (_curGameState.LastRoomID != 0)
                {
                    _curGameState.RoomID = _curGameState.LastRoomID;
                    _curGameState.LastRoomID = 0;
                }

                if (_curGameState.RoomID == 0)
                {
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, "no valid room to join");
                    return;
                }
            }
            if (_curGameState.CurState < EGameState.Inited)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, $"StartJoinRoom, but game state {_curGameState.CurState} is invalid");
                return;
            }
            CallOnStatusChange(EGameState.RoomJoining, $"request to join room {_curGameState.RoomID} ...");
            var roomOptions = GameUtils.GetRoomOptions(_curGameState.RoomID.ToString(), null, null, null, null, null);
            RoomService.Join2(_curGameState.RoomID, roomOptions).OnComplete(ProcessRoomJoin2);
        }

        public void StartLeaveRoom()
        {
            if (_curGameState.RoomID == 0)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, "no valid room to leave");
                return;
            }
            if (_curGameState.CurState < EGameState.RoomJoining)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, "not InRoom, skip this request");
                return;
            }
            CallOnStatusChange(EGameState.RoomLeaving, $"request to Leaving room {_curGameState.RoomID} ...");
            RoomService.Leave(_curGameState.RoomID).OnComplete(ProcessRoomLeave);
        }

        public ulong GetRoomID()
        {
            if (_curGameState.CurState != EGameState.InRoom)
            {
                return 0;
            }
            return _curGameState.RoomID;
        }

        private void OnGameInitialize(Message<GameInitializeResult> msg)
        {
            if (msg == null)
            {
                EndPicoGame($"OnGameInitialize Failed: message is null");
                return;
            }

            if (msg.IsError)
            {
                EndPicoGame($"GameInitialize Failed: {msg.Error.Code}, {msg.Error.Message}");
            }
            else
            {
                if (msg.Data == GameInitializeResult.Success)
                {
                    CallOnStatusChange(EGameState.Inited, "game init succeed");
                    if (_autoMatchmaking)
                    {
                        PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"auto start matchmaking in OnGameInitialize ...");
                        StartMatchmaking();
                    }
                }
                else
                {
                    EndPicoGame($"GameInitialize: failed({msg.Data}) please re-initialize");
                }
            }
        }

        public void SetAutoRestartFlag()
        {
            _autoRestartNetcodeOnHostLeave = true;
        }


        private void CallOnStatusChange(EGameState curState, string statusDesc)
        {
            var lastState = _curGameState.CurState;
            _curGameState.CurState = curState;
            if (_curGameState.CurState == EGameState.NotInited)
            {
                if (_curGameState.LastRoomID != 0)
                {
                    _curGameState.CurState = EGameState.MatchFound;
                }
            }

            string matchedInfo = "";
            if (curState == EGameState.MatchFound)
            {
                matchedInfo = GameUtils.GetRoomLogData(_curGameState.RoomData);
            }
            OnStatusChange?.Invoke(lastState, _curGameState.CurState, statusDesc, _curGameState.UserInfo?.ID, matchedInfo);
        }

        private IEnumerator InitPicoPlatformAndGame(TextAsset mock_config)
        {
            _isLastSessionMsg = true;
            yield return null;
            if (!CoreService.Initialized)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"InitPicoPlatformAndGame start coreservice init ...");
                try
                {
                    CoreService.Initialize(null);
                }
                catch (Exception e)
                {
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"exception init pico Core {e}");
                }
                if (!CoreService.Initialized)
                {
                    CallOnStatusChange(EGameState.NotInited, "pico SDK init failed ...");
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, "pico initialize failed");
                    yield break;
                }
            }
            CallOnStatusChange(_curGameState.CurState, "CoreService get access token started ...");
            UserService.GetAccessToken().OnComplete(delegate (Message<string> message)
            {
                CallOnStatusChange(_curGameState.CurState, "get access token finished");
                if (message.IsError)
                {
                    var err = message.Error;
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, $"Got access token error {err.Message} code={err.Code}");
                    CallOnStatusChange(EGameState.NotInited, "CoreService get access token failed");
                    return;
                }
                CallOnStatusChange(_curGameState.CurState, "get access token succeed");
                string accessToken = message.Data;
                UserService.GetLoggedInUser().OnComplete(delegate (Message<User> userInfo)
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
                    CallOnStatusChange(EGameState.UserGot, "GetLoggedInUser complete, player openID");

                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"got accessToken {accessToken}, GameInitialize begin");
                    CallOnStatusChange(EGameState.InGameIniting, "Start game initialization, player openID: userInfo.Data.ID");
                    CoreService.GameUninitialize();
                    Task<GameInitializeResult> request;
                    _isLastSessionMsg = false;
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"[PPF_GAME_Unity] init with accessToken:{accessToken}, self ID: {userInfo.Data.ID}");
                    request = CoreService.GameInitialize(accessToken);

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

        private void GameConnectionEventCallback(Message<GameConnectionEvent> msg)
        {
            if (IsOldSessionCallback("connect_event"))
            {
                return;
            }
            var state = msg.Data;
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"SimplePicoSDKUser, OnGameConnectionEvent: {state}");
            if (state == GameConnectionEvent.Connected)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "GameConnection: success");
            }
            else if (state == GameConnectionEvent.Closed)
            {
                StopNetcode("GameConnection: failed Please re-initialize");
            }
            else if (state == GameConnectionEvent.GameLogicError)
            {
                StopNetcode("GameConnection: failed After successful reconnection, the logic state is found to be wrong, please re-initialize");
            }
            else if (state == GameConnectionEvent.Lost)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"######, connect lost, wait reconnecting");
                if (0 == _restartTimeoutTick)
                {
                    /*
                        A single tick represents one hundred nanoseconds or one ten-millionth of a second. There are 10,000 ticks in a millisecond (see TicksPerMillisecond) and 10 million ticks in a second.                      
                     */
                    _restartTimeoutTick = DateTime.Now.Ticks + 10 * 1000 * 10000;
                }
            }
            else if (state == GameConnectionEvent.Resumed)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"######, connect resumed");
                _restartTimeoutTick = 0;
            }
            else if (state == GameConnectionEvent.KickedByRelogin)
            {
                StopNetcode("GameConnection: login in other device, try reinitialize later");
            }
            else if (state == GameConnectionEvent.KickedByGameServer)
            {
                StopNetcode("GameConnection: be kicked by server, try reinitialize later");
            }
            else
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "GameConnection: unknown error");
            }
        }

        private void RequestFailedCallback(Message<GameRequestFailedReason> msg)
        {
            if (IsOldSessionCallback("request_failed"))
            {
                return;
            }
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"OnRequestFailed: {msg.Data}");
        }

        private void GameStateResetCallback(Message msg)
        {
            if (IsOldSessionCallback("game_state_reset"))
            {
                return;
            }
            StopNetcode("local game state disaccord with that of the server, please re-initialize");
        }
    }

}

