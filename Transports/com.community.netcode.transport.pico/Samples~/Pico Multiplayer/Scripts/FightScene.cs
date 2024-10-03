using UnityEngine;

namespace Netcode.Transports.Pico.Sample.PicoMultiplayer
{
    public class FightScene : MonoBehaviour
    {
        private ExternalModeSDKUser _picoUser;

        // Start is called before the first frame update
        void Start()
        {
            _picoUser = FindObjectOfType<ExternalModeSDKUser>();
            _picoUser.SetAutoRestartFlag();
            _picoUser.StartNetcode();
        }

        private void OnDestroy()
        {
            _picoUser.StopNetcode("fight scene destroy");
            _picoUser = null;
        }
    }
}
