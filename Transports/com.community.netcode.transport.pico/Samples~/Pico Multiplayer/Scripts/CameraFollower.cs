using UnityEngine;
using UnityEngine.SceneManagement;

namespace Netcode.Transports.Pico.Sample.PicoMultiplayer
{
    public class CameraFollower : MonoBehaviour
    {
        private Transform _target;
        public Vector3 Offset;
        public float SmoothSpeed = 0.1f;

        public void SetTarget(Transform target)
        {
            _target = target;
        }

        private void LateUpdate()
        {
            if (_target)
            {
                SmoothFollow();
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += ResetCameraPosition;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= ResetCameraPosition;
        }

        private void ResetCameraPosition(Scene scene, LoadSceneMode mode)
        {
            transform.position = Offset;
            transform.rotation = Quaternion.identity;
        }

        private void SmoothFollow()
        {
            Vector3 targetPos = _target.position + Offset;
            Vector3 smoothFollow = Vector3.Lerp(transform.position, targetPos, SmoothSpeed);

            transform.position = smoothFollow;
            transform.LookAt(_target);
        }
    }
}