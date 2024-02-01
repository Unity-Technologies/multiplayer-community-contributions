using Unity.Netcode;
using UnityEngine;

namespace Netcode.Transports.Pico.Sample.PicoMultiplayer
{
    public class InputsReader : NetworkBehaviour
    {
        public Vector2 MoveInput;
        public Vector2 OrbitInput;
        public bool JumpInput;

        private UnityEngine.XR.InputDevice _xrInputs;
        private Vector2 _axis2D = Vector2.zero;
        private bool _primaryButton;

        public void Start()
        {
            //ref: D:\works\pico\PICO Unity Integration SDK v212\Runtime\Scripts\Controller\PXR_ControllerAnimator.cs
            _xrInputs = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
        }

        public void Update()
        {
            if (IsOwner)
            {
                CheckXRInput();
            }
        }

        public void CheckXRInput()
        {
            if (!_xrInputs.isValid)
            {
                return;
            }

            _xrInputs.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out _axis2D);
            _xrInputs.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out _primaryButton);
            if (_axis2D.magnitude > 0.001)
            {
                MoveInput = _axis2D;
            }
            else
            {
                MoveInput = Vector2.zero;
            }

            if (_primaryButton)
            {
                JumpInput = true;
            }
            else
            {
                JumpInput = false;
            }
        }

        public void OnMove(UnityEngine.InputSystem.InputValue value)
        {
            //Debug.Log($"OnMove: {value.Get<Vector2>()}");
            MoveInput = value.Get<Vector2>();
        }

        public void OnJump(UnityEngine.InputSystem.InputValue value)
        {
            //Debug.Log($"OnJump: {value.isPressed}");
            JumpInput = value.isPressed;
        }

        public void OnOrbit(UnityEngine.InputSystem.InputValue value)
        {
            //Debug.Log($"OnOrbit: {value.isPressed}");
            OrbitInput = value.Get<Vector2>();
        }

        public void OnDeviceLost(UnityEngine.InputSystem.PlayerInput input)
        {
            Debug.Log($"OnDeviceLost: {input.name}");
        }

        public void OnDeviceRegained(UnityEngine.InputSystem.PlayerInput input)
        {
            Debug.Log($"OnDeviceRegained: {input.name}");
        }

        public void OnControlsChanged(UnityEngine.InputSystem.PlayerInput input)
        {
            Debug.Log($"OnControlsChanged: {input.name}");
        }
    }

}