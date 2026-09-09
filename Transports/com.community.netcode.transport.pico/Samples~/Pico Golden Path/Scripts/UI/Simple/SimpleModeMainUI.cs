using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Unity.Netcode;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

namespace Netcode.Transports.Pico.Samples.PicoGoldenPath
{
    public class SimpleModeMainUI : MonoBehaviour
    {
        public SimpleModeRoomPanel InRoomPanel;
        
        // Start is called before the first frame update
        void Start()
        {
            PicoTransport picoTransport = NetworkManager.Singleton.NetworkConfig.NetworkTransport as PicoTransport;
            bool isPico = picoTransport != null;
            if (isPico)
            {
                if (picoTransport.WorkMode != PicoTransport.EWorkMode.Simple)
                {
                    picoTransport.WorkMode = PicoTransport.EWorkMode.Simple;
                    picoTransport.SimpleModeInfo.roomName = "test_room_name_abc";
                    picoTransport.SimpleModeInfo.password = "";
                }
            }
            else
            {
                Debug.Assert(false, "set netcode transport to pico please");
            }
        }

        // Update is called once per frame
        void Update()
        {
        }

        //void InitPanel()
        //{
        //    InRoomPanel.gameObject?.SetActive(true);
        //}
    }

}
