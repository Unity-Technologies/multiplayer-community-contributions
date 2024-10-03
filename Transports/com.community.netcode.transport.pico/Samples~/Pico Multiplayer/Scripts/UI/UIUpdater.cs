using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace Netcode.Transports.Pico.Sample.PicoMultiplayer
{

    [RequireComponent(typeof(LogToUI))]
    public class UIUpdater : MonoBehaviour
    {
        const string ButtonName = "ButtonText";

        private TMP_Text _buttonText;
        private ExternalModeSDKUser _picoUser;
        private ExternalModeSDKUser.EGameState _curState;

        private delegate void ButtonClickHandler();

        struct StateInfo
        {
            public bool buttonValid;
            public string buttonText;
            public ButtonClickHandler clickHandler;

            public StateInfo(bool inValid, string inText, ButtonClickHandler inHandler)
            {
                buttonValid = inValid;
                buttonText = inText;
                clickHandler = inHandler;
            }
        }

        Dictionary<ExternalModeSDKUser.EGameState, StateInfo> stateInfos;

        // Start is called before the first frame update
        void Start()
        {
            TMP_Text[] candidates = GetComponentsInChildren<TMP_Text>();
            for (int i = 0; i < candidates.Length; ++i)
            {
                TMP_Text tmp = candidates[i];
                if (tmp.name == ButtonName)
                {
                    //for later button text modification;
                    _buttonText = candidates[i];
                }
            }

            _picoUser = GetComponent<ExternalModeSDKUser>();
            if (!_picoUser)
            {
                Debug.LogError("can not find PicoSDKUser in the GameObject");
                return;
            }

            stateInfos = new Dictionary<ExternalModeSDKUser.EGameState, StateInfo>
            {
                {
                    ExternalModeSDKUser.EGameState.NotInited,
                    new StateInfo(true, "StartMatchmaking", StartMatchmaking)
                },
                { ExternalModeSDKUser.EGameState.InIniting, new StateInfo(false, "InPlatformIniting...", Empty) },
                { ExternalModeSDKUser.EGameState.UserGot, new StateInfo(false, "UserIsValid...", Empty) },
                { ExternalModeSDKUser.EGameState.InGameIniting, new StateInfo(false, "InGameIniting...", Empty) },
                { ExternalModeSDKUser.EGameState.Inited, new StateInfo(true, "StartMatchmaking...", Empty) },
                { ExternalModeSDKUser.EGameState.InMatching, new StateInfo(false, "InMatching...", Empty) },
                { ExternalModeSDKUser.EGameState.MatchFound, new StateInfo(true, "JoinRoom", _picoUser.StartJoinRoom) },
                { ExternalModeSDKUser.EGameState.RoomLeaving, new StateInfo(true, "InLeaveRoom...", Empty) },
                { ExternalModeSDKUser.EGameState.RoomLeft, new StateInfo(true, "RoomLeft...", Empty) },
                { ExternalModeSDKUser.EGameState.RoomJoining, new StateInfo(false, "JoinRooming...", Empty) },
                { ExternalModeSDKUser.EGameState.InRoom, new StateInfo(false, "InRoom...", Empty) },
            };
            _picoUser.OnStatusChange += HandleStatusChange;
        }

        private void OnDestroy()
        {
            if (_picoUser)
            {
                _picoUser.OnStatusChange -= HandleStatusChange;
            }
        }

        public void HandleButtonClick()
        {
            if (!_picoUser)
            {
                Debug.LogWarning($"picoUser is not valid, skip this request");
                return;
            }
            StateInfo tmpinfo = stateInfos[_curState];
            tmpinfo.clickHandler();
        }

        void StartMatchmaking()
        {
            TextAsset mockConfig = null;
#if UNITY_EDITOR
            if ((Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor))
            {
                // Automatically start server if this is the original editor
                mockConfig = Resources.Load<TextAsset>("PicoSdkPCConfig");
            }
#endif
            _picoUser.StartPicoGame(mockConfig);
        }

        void Empty()
        {
            Debug.LogWarning("no action");
        }

        void HandleStatusChange(ExternalModeSDKUser.EGameState oldState, ExternalModeSDKUser.EGameState newState,
            string inDesc, string inOpenID, string matchInfo)
        {
            Debug.Log($"state change {_curState} > {newState}, {inOpenID}, {inDesc}");
            _curState = newState;
            StateInfo stateInfo = stateInfos[_curState];
            _buttonText.text = stateInfo.buttonText;
            if (newState < ExternalModeSDKUser.EGameState.MatchFound)
            {
                //匹配达成以前的状态，需要清空之前的匹配信息
                GetComponent<LogToUI>().SetMatchInfo("");
            }

            if (newState == ExternalModeSDKUser.EGameState.MatchFound)
            {
                GetComponent<LogToUI>().SetMatchInfo(matchInfo);
                _picoUser.StartJoinRoom();
            }
        }

    }

}