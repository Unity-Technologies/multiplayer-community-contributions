using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

namespace Netcode.Transports.Pico.Samples.PicoGoldenPath
{
    public class MatchmakingMainUI : MonoBehaviour
    {
        private enum ETransportType
        {
            PicoTransport = 1,
            PhotonTransport = 2,
        }

        public Button InitPicoSDKBtn;
        public Button JoinBtn;
        public TextMeshProUGUI CurStatus;
        public TextMeshProUGUI SelfOpenID;
        public MatchmakingRoomPanel InRoomPanel;
        
        private ExternalModeSDKUser _picoSDKUser;

        // Start is called before the first frame update
        void Start()
        {
            _picoSDKUser = FindObjectOfType<ExternalModeSDKUser>();
            PicoTransport picoTransport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as PicoTransport;
            bool isPico = picoTransport != null;
            if (isPico)
            {
                picoTransport.WorkMode = PicoTransport.EWorkMode.ExternalRoom;
                InitPicoSDKBtn.onClick.AddListener(StartPicoInExternalMode);
            }
            else
            {
                Debug.Assert(false, "set netcode transport to pico please");
            }
            CurStatus.text = SceneManager.GetActiveScene().name + ":" + "press init button to start";
            JoinBtn.onClick.AddListener(_picoSDKUser.StartJoinRoom);
            _picoSDKUser.OnStatusChange += OnStatusChage;
        }

        // Update is called once per frame
        void Update()
        {
        }

        void StartPicoInExternalMode()
        {
            //mode 'external room'
            TextAsset mockConfig = null;
#if UNITY_EDITOR
            if ((Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor))
            {
                // Automatically start server if this is the original editor
                mockConfig = Resources.Load<TextAsset>("PicoSdkPCConfig");
            }
#endif
            _picoSDKUser.StartPicoGame(mockConfig);
        }

        void OnStatusChage(ExternalModeSDKUser.EGameState oldState, ExternalModeSDKUser.EGameState curState, string statusDesc, string selfOpenID, string roomDesc)
        {
            if (null == selfOpenID)
            {
                selfOpenID = "not loggined";
            }
            if (null == InitPicoSDKBtn || null == JoinBtn || null == InRoomPanel)
            {
                Debug.Log($"OnStatusChage to {curState}, ui object is null");
                return;
            }
            CurStatus.text = SceneManager.GetActiveScene().name + ":" + statusDesc;
            SelfOpenID.text = "selfUID:" + selfOpenID;

            Debug.Log($"state from {oldState} to {curState}, reason {statusDesc}");
            if (curState == oldState)
            {
                return;
            }
            if (curState < ExternalModeSDKUser.EGameState.Inited || curState == ExternalModeSDKUser.EGameState.RoomLeft)
            {
                InitPicoSDKBtn.gameObject?.SetActive(true);
                JoinBtn.gameObject?.SetActive(false);
                InRoomPanel.gameObject?.SetActive(false);
            }
            if (curState == ExternalModeSDKUser.EGameState.InMatching)
            {
                InitPicoSDKBtn.gameObject?.SetActive(false);
                JoinBtn.gameObject.SetActive(false);
                InRoomPanel.gameObject?.SetActive(false);
            }
            if (curState == ExternalModeSDKUser.EGameState.MatchFound)
            {
                InitPicoSDKBtn.gameObject?.SetActive(false);
                JoinBtn.gameObject?.SetActive(true);
                InRoomPanel.gameObject?.SetActive(false);
            }
            if (curState == ExternalModeSDKUser.EGameState.InRoom)
            {
                InitPicoSDKBtn.gameObject?.SetActive(false);
                JoinBtn.gameObject?.SetActive(false);
                InRoomPanel.gameObject?.SetActive(true);
            }

            if (curState == ExternalModeSDKUser.EGameState.InRoom)
            {
                _picoSDKUser.StartNetcode();
            }
        }
    }

}
