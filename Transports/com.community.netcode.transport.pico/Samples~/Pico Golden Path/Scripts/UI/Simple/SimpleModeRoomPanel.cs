using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Netcode.Transports.Pico.Samples.PicoGoldenPath
{
    public class SimpleModeRoomPanel : MonoBehaviour
    {
        public Button MoveButton;
        public Button StartRoomButton;
        public Button JoinRoomButton;
        public Button ExitRoomButton;
        public TextMeshProUGUI StatusText;

        private void Start()
        {
            MoveButton.onClick.AddListener(OnMoveButton);
            StartRoomButton.onClick.AddListener(OnStartRoomButton);
            JoinRoomButton.onClick.AddListener(OnJoinRoomButton);
            ExitRoomButton.onClick.AddListener(OnExitRoomButton);
        }

        // Update is called once per frame
        void Update()
        {
            bool isNetcodeStarted = NetworkManager.Singleton.isActiveAndEnabled;
            bool isHostOrClient = isNetcodeStarted && NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient;
            if (isHostOrClient)
            {
                StartRoomButton.gameObject.SetActive(false);
                JoinRoomButton.gameObject.SetActive(false);
                ExitRoomButton.gameObject.SetActive(true);
            } else
            {
                StartRoomButton.gameObject.SetActive(true);
                JoinRoomButton.gameObject.SetActive(true);
                ExitRoomButton.gameObject.SetActive(false);
            }
            if (!(NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient))
            {
                StatusText.text = "Netcode not start";
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
        }

        public void OnStartRoomButton()
        {
            NetworkManager.Singleton.StartHost();
            return;
        }

        public void OnJoinRoomButton()
        {
            NetworkManager.Singleton.StartClient();
            return;
        }

        public void OnExitRoomButton()
        {
            NetworkManager.Singleton.Shutdown();
            return;
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
                    return;
                }
                var player = playerObject.GetComponent<PlayerBehaviour>();
                player.Move();
            }
        }        
    }

}
