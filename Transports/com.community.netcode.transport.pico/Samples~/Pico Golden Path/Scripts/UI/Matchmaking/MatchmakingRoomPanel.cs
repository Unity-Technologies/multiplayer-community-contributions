using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Netcode.Transports.Pico.Samples.PicoGoldenPath
{
    public class MatchmakingRoomPanel : MonoBehaviour
    {
        public Button MoveButton;
        public Button LeaveButton;
        public TextMeshProUGUI ButtonText;
        public TextMeshProUGUI StatusText;

        private ExternalModeSDKUser _picoSDKUser;

        private void Start()
        {
            _picoSDKUser = FindObjectOfType<ExternalModeSDKUser>();
            MoveButton.onClick.AddListener(OnMoveButton);
            LeaveButton.onClick.AddListener(_picoSDKUser.StartLeaveRoom);
        }

        // Update is called once per frame
        void Update()
        {
            if (!(NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient))
            {
                return;
            }
            StatusText.text = "Netcode mode:";
            if (NetworkManager.Singleton.IsHost)
            {
                StatusText.text += "HOST";
            }
            else if (NetworkManager.Singleton.IsServer)
            {
                StatusText.text += "SERVER";
            }
            else
            {
                StatusText.text += "CLIENT";
            }
            if (NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
            {
                ButtonText.text = "Move";
            }
            else
            {
                ButtonText.text = "Submit position";
            }
        }

        public void OnMoveButton()
        {
            if (NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
            {
                foreach (ulong uid in NetworkManager.Singleton.ConnectedClientsIds)
                    NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(uid)
                        .GetComponent<PlayerBehaviour>().Move();
            }
            else
            {
                var playerObject = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                if (!playerObject)
                {
                    Debug.LogError("player object is not spawned now");
                }
                var player = playerObject.GetComponent<PlayerBehaviour>();
                player.Move();
            }
        }        
    }

}
