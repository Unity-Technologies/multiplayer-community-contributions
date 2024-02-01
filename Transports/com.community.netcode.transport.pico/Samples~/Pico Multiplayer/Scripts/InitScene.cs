using UnityEngine;

namespace Netcode.Transports.Pico.Sample.PicoMultiplayer
{
    public class InitScene : MonoBehaviour
    {
        private void Awake()
        {
            SampleApplication.GetInstance();
        }
    }
}
