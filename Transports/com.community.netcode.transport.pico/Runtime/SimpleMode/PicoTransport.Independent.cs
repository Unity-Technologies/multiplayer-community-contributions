using System;
using Unity.Netcode;
using UnityEngine;

namespace Netcode.Transports.Pico
{
    public partial class PicoTransport
    {
        private InnerPicoSDKUser.EGameState _currentGameState = InnerPicoSDKUser.EGameState.NotInited;

        [Serializable]
        public class SimpleModeConfig
        {
            public string roomName;
            public string password;
        }

        public SimpleModeConfig SimpleModeInfo;
        InnerPicoSDKUser _picoSDKUser = null;
        bool _joinRoomWaitGameInit = false;

        void PrepareIndependent()
        {
            TextAsset mockConfig = null;
#if UNITY_EDITOR
            if ((Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor))
            {
                // Automatically start server if this is the original editor
                mockConfig = Resources.Load<TextAsset>("PicoSdkPCConfig");
            }
#endif
            _roomLeftOnce = false;
            _picoSDKUser = FindObjectOfType<InnerPicoSDKUser>();
            if (_picoSDKUser == null)
            {
                _picoSDKUser = this.gameObject.AddComponent<InnerPicoSDKUser>();
            }
            this.OnPicoTransportEvent -= _picoSDKUser.HandlePicoTransportEvent;
            this.OnPicoTransportEvent += _picoSDKUser.HandlePicoTransportEvent;
            _picoSDKUser.OnPicoSdkNotify -= HandlePicoNotify;
            _picoSDKUser.OnPicoRoomMessage -= OnPkgRecved;
            _picoSDKUser.OnPicoSdkNotify += HandlePicoNotify;
            _picoSDKUser.OnPicoRoomMessage += OnPkgRecved;
            PicoTransport.PicoTransportLog(LogLevel.Info, "PrepareIndependent, StartPicoGame ...");
            _picoSDKUser.StartPicoGame(mockConfig);
        }

        void AfterGameInited()
        {
            if (_joinRoomWaitGameInit)
            {                
                JoinNamedRoom();
            }
        }

        void ShutdownNetcode()
        {
            if (NetworkManager.Singleton)
            {
                PicoTransportLog(LogLevel.Info, $"<--------------------------------------------");
                NetworkManager.Singleton.Shutdown();
            } else
            {
                AfterShutdown();
            }
        }

        void AfterRoomJoined()
        {
            if (NetworkManager.Singleton.IsServer)
            {
                //因为目前没有单create named room接口，所以StartHost时，需要检测自身是否为房主，如果不是，则停止netcode
                if (_picoSDKUser.GetSelfLoggedInUID() != _picoSDKUser.GetTransportRoomInfo().OwnerUID)
                {
                    PicoTransportLog(LogLevel.Error, "AfterRoomJoined, room already exist, StartHost failed");
                    StopPicoTransport();
                    return;
                }
            }
            SetRoomProvider(_picoSDKUser, _picoSDKUser.GetSelfLoggedInUID(), _picoSDKUser.GetTransportRoomInfo());
        }

        void AfterRoomLeft()
        {
            if (_roomLeftOnce)
            {
                return;
            }
            _roomLeftOnce = true;
            PicoTransport.PicoTransportLog(LogLevel.Info, $"AfterRoomLeft: current state {_currentGameState}, previousRoomID {_picoSDKUser.GetPreviousRoomID()}");
            StopPicoTransport();
        }

        void AfterNotInited()
        {
            //if (_currentGameState > InnerPicoSDKUser.GameState.Inited)
            {
                if (0 != _picoSDKUser.GetPreviousRoomID())
                {
                    PicoTransport.PicoTransportLog(LogLevel.Info, $"AfterNotInited: call OnPlayerLeaveRoom {_picoSDKUser.GetPreviousRoomID()}");
                    OnSelfLeaveRoom(_picoSDKUser.GetSelfLoggedInUID(), _picoSDKUser.GetPreviousRoomID());
                    return;
                }
                AfterRoomLeft();
            }
        }

        void JoinNamedRoom()
        {
            if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsHost)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, ">>>NetworkManager self is server, create and join");
                _picoSDKUser.StartJoinNamedRoom(SimpleModeInfo.roomName, SimpleModeInfo.password, true);
            } else
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, ">>>NetworkManager self is client, join");
                _picoSDKUser.StartJoinNamedRoom(SimpleModeInfo.roomName, SimpleModeInfo.password, false);
            }
        }

        void InitIndependent()
        {
            _joinRoomWaitGameInit = true;
            switch (_currentGameState)
            {
                case InnerPicoSDKUser.EGameState.Inited:
                    PicoTransport.PicoTransportLog(LogLevel.Warn, $"InitIndependent, already in GameState.Inited, start join named room ...");
                    AfterGameInited();
                    break;
                default:
                    return;
            }
            return;
        }

        void UpdateIndependent()
        {
            return;
        }

        void HandleRoomEvent(TransportPicoRoomInfo.ERoomEvent roomEvent, string eventDesc, TransportRoomInfo roomInfo)
        {
            switch (roomEvent)
            {
                case TransportPicoRoomInfo.ERoomEvent.OwnerLeaveRoom:
                case TransportPicoRoomInfo.ERoomEvent.SelfLeaveRoom:
                case TransportPicoRoomInfo.ERoomEvent.UpdateRoomInfo:
                    {
                        if ( (null == roomInfo) || (0 == roomInfo.RoomID) || (roomInfo.OwnerUID == 0))
                        {
                            AfterRoomLeft();
                        } else
                        {
                            OnRoomInfoUpdate(roomInfo);
                        }                        
                    }
                    break;
            }
            return;
        }

        void HandlePicoNotify(InnerPicoSDKUser.ESDKUserEvent inEvent, InnerPicoSDKUser.EGameState oldState, InnerPicoSDKUser.EGameState curState, string eventDesc)
        {
            _currentGameState = curState;

            if (inEvent == InnerPicoSDKUser.ESDKUserEvent.RoomInfoUpdate)
            {
                HandleRoomEvent(TransportPicoRoomInfo.ERoomEvent.UpdateRoomInfo, "room update from server", _picoSDKUser.GetTransportRoomInfo());
                return;
            }

            if (curState == oldState)
            {
                return;
            }
            switch (_currentGameState)
            {
                case InnerPicoSDKUser.EGameState.Inited:
                    if (oldState < InnerPicoSDKUser.EGameState.Inited)
                    {
                        AfterGameInited();
                    }
                    else
                    {
                        if (NetworkManager.Singleton != null)
                        {
                            PicoTransport.PicoTransportLog(LogLevel.Info, $"HandlePicoNotify: return to GameState.Inited: NetworkManager is running, stopped it now");
                            StopPicoTransport();
                        }
                    }
                    break;
                case InnerPicoSDKUser.EGameState.InRoom:
                    PicoTransport.PicoTransportLog(LogLevel.Info, $"HandlePicoNotify of GameState.InRoom, room join succeed: {eventDesc}");
                    AfterRoomJoined();
                    break;
                case InnerPicoSDKUser.EGameState.NotInited:
                    PicoTransport.PicoTransportLog(LogLevel.Info, $"HandlePicoNotify of pico game service init failed: {eventDesc}");
                    ShutdownNetcode();
                    break;
            }
        } // void OnStatusChange
    } // public partial class PicoTransport
}
