using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace Netcode.Transports.Pico.Sample.PicoMultiplayer
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerController : NetworkBehaviour
    {
        public float MoveSpeed = 5.33f;
        public float RotationSmoothTime = 0.12f;
        public float SpeedChangeRate = 10.0f;
        private float JumpHeight = 1.2f;
        private float Gravity = -15.0f;
        private float JumpTimeout = 0.50f;
        private float FallTimeout = 0.15f;
        private bool Grounded = true;
        private float GroundedOffset = -0.14f;
        private float GroundedRadius = 0.28f;
        public LayerMask GroundLayers;

        // player
        private float _speed;
        private float _animationBlend;
        private float _targetRotation = 0.0f;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;

        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;
        private Dictionary<int, (float f, bool b)> _animPropCache = new Dictionary<int, (float f, bool b)>();

        private Animator _animator;
        private CharacterController _controller;
        private InputsReader _inputReader;
        private CameraFollower _cameraFollower;

        private bool _hasAnimator;

        private void Start()
        {
            AssignAnimationIDs();
            _hasAnimator = TryGetComponent(out _animator);
            _controller = GetComponent<CharacterController>();
            _inputReader = GetComponent<InputsReader>();
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;
            if (IsOwner)
            {
                _cameraFollower = FindObjectOfType<CameraFollower>();
                _cameraFollower.SetTarget(this.transform);
            }
        }

        public override void OnNetworkSpawn()
        {
            enabled = IsOwner;
            GetComponent<PlayerInput>().enabled = IsOwner;
        }

        private void Update()
        {
            if (!_controller.enabled) return;
            _hasAnimator = TryGetComponent(out _animator);
            JumpAndGravity();
            GroundedCheck();
            Move();
        }

        private void AssignAnimationIDs()
        {
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        }

        private void GroundedCheck()
        {
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
            if (_hasAnimator)
            {
                SetAnimatorBool(_animIDGrounded, Grounded);
            }
        }

        private void Move()
        {
            float targetSpeed = MoveSpeed;
            if (_inputReader.MoveInput == Vector2.zero) targetSpeed = 0.0f;
            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;
            float speedOffset = 0.1f;
            float inputMagnitude = _inputReader.MoveInput.magnitude;

            if (currentHorizontalSpeed < targetSpeed - speedOffset ||
                currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);
                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }
            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);

            //input speed
            Vector3 inputDirection = new Vector3(_inputReader.MoveInput.x, 0.0f, _inputReader.MoveInput.y).normalized;
            if (_inputReader.MoveInput != Vector2.zero)
            {
                _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + Camera.main.transform.eulerAngles.y;
                float rotation = Mathf.SmoothDampAngle(transform.eulerAngles.y, _targetRotation, ref _rotationVelocity, RotationSmoothTime);
                transform.rotation = Quaternion.Euler(0.0f, rotation, 0.0f);
            }
            Vector3 targetDirection = Quaternion.Euler(0.0f, _targetRotation, 0.0f) * Vector3.forward;
            Vector3 moveVec = targetDirection.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime;
            _controller.Move(moveVec);
            //update animator
            if (_hasAnimator)
            {
                SetAnimatorFloat(_animIDSpeed, _animationBlend);
                SetAnimatorFloat(_animIDMotionSpeed, inputMagnitude);
            }
        }

        private void JumpAndGravity()
        {
            if (Grounded)
            {
                _fallTimeoutDelta = FallTimeout;
                if (_hasAnimator)
                {
                    SetAnimatorBool(_animIDJump, false);
                    SetAnimatorBool(_animIDFreeFall, false);
                }
                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }
                // check jump cmd
                if (_inputReader.JumpInput && _jumpTimeoutDelta <= 0.0f)
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                    if (_hasAnimator)
                    {
                        SetAnimatorBool(_animIDJump, true);
                    }
                }
                // check jump timeout
                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                _jumpTimeoutDelta = JumpTimeout;
                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }
                else
                {
                    if (_hasAnimator)
                    {
                        SetAnimatorBool(_animIDFreeFall, true);
                    }
                }
                _inputReader.JumpInput = false;
            }
            if (_verticalVelocity < _terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);
            if (Grounded) Gizmos.color = transparentGreen;
            else Gizmos.color = transparentRed;
            Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
        }

        [ServerRpc(RequireOwnership = true)]
        private void SetAnimatorFloatServerRpc(int id, float value)
        {
            _hasAnimator = _hasAnimator || TryGetComponent(out _animator);
            if (_hasAnimator)
            {
                _animator.SetFloat(id, value);
            }
        }

        [ServerRpc(RequireOwnership = true)]
        private void SetAnimatorBoolServerRpc(int id, bool value)
        {
            _hasAnimator = _hasAnimator || TryGetComponent(out _animator);
            if (_hasAnimator)
            {
                _animator.SetBool(id, value);
            }
        }

        private void SetAnimatorFloat(int id, float value)
        {
            if (_hasAnimator && _animPropCache.TryGetValue(id, out var current) && current.f.IsCloseTo(value))
            {
                return;
            }

            SetAnimatorFloatServerRpc(id, value);
            _animPropCache[id] = (value, false);
            if (_hasAnimator)
            {
                _animator.SetFloat(id, value);
            }
        }

        private void SetAnimatorBool(int id, bool value)
        {
            if (_hasAnimator && _animPropCache.TryGetValue(id, out var current) && current.b == value)
            {
                return;
            }
            SetAnimatorBoolServerRpc(id, value);
            _animPropCache[id] = (0.0f, value);
            if (_hasAnimator)
            {
                _animator.SetBool(id, value);
            }
        }
    }
}