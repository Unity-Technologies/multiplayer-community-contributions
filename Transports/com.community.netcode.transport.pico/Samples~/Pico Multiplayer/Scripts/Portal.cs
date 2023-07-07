using UnityEngine;

namespace Netcode.Transports.Pico.Sample.PicoMultiplayer
{
    public class Portal : MonoBehaviour
    {
        private SampleApplication _application;

        private void Start()
        {
            _application = SampleApplication.GetInstance();
        }

        private void OnTriggerEnter(Collider other)
        {
            var playerState = other.GetComponent<PlayerState>();
            if (!playerState)
            {
                return;
            }
            if (!playerState.IsSelfPlayer()) return;
            _application.OnPortalEnter();
        }
    }
}
