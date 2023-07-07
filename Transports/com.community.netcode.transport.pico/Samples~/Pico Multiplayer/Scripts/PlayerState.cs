using UnityEngine;
using Unity.Collections;
using Unity.Netcode;
using TMPro;
using UnityEngine.UI;

public class PlayerState : NetworkBehaviour
{
    public Renderer PlayerMeshRenderer;
    public Transform UsernameCanvas;

    private NetworkVariable<Color> _nvColor = new NetworkVariable<Color>();
    private NetworkVariable<FixedString128Bytes> _nvUsername = new NetworkVariable<FixedString128Bytes>();
    private NetworkVariable<bool> _nvHostFlag = new NetworkVariable<bool>(true);

    private TMP_Text _uiUsername;
    private Image _uiMasterIcon;
    private Transform _cameraRigForUIHostFlag;
    private LocalPlayerState _localPlayerState => IsOwner ? LocalPlayerState.Instance : null;

    public bool IsSelfPlayer()
    {
        return IsOwner;
    }

    private void OnEnable()
    {
        _uiUsername = UsernameCanvas.GetComponentInChildren<TMP_Text>();
        _uiMasterIcon = transform.GetComponentInChildren<Image>();

        _nvColor.OnValueChanged += OnColorChanged;
        _nvUsername.OnValueChanged += OnUsernameChanged;
        _nvHostFlag.OnValueChanged += OnMasterclientChanged;
    }

    private void OnDisable()
    {
        _nvColor.OnValueChanged -= OnColorChanged;
        _nvUsername.OnValueChanged -= OnUsernameChanged;
        _nvHostFlag.OnValueChanged -= OnMasterclientChanged;
    }

    private void Start()
    {
        _cameraRigForUIHostFlag = Camera.main.GetComponentInParent<Transform>();
        OnColorChanged(_nvColor.Value, _nvColor.Value);
        OnUsernameChanged(_nvUsername.Value, _nvUsername.Value);
        OnMasterclientChanged(_nvHostFlag.Value, _nvHostFlag.Value);
        if (IsSelfPlayer())
        {
            _localPlayerState.OnSelfStateChange += UpdateData;
            UpdateData();
        }
    }

    private void Update()
    {
        UsernameCanvas.rotation = Quaternion.LookRotation(UsernameCanvas.position - _cameraRigForUIHostFlag.position);
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        if (IsSelfPlayer() && _localPlayerState)
        {
            _localPlayerState.transform.position = transform.position;
            _localPlayerState.transform.rotation = transform.rotation;
            _localPlayerState.OnSelfStateChange -= UpdateData;
        }
    }

    private void OnColorChanged(Color oldColor, Color newColor)
    {
        foreach (Material mat in PlayerMeshRenderer.materials)
        {
            mat.color = newColor;
        }
    }

    private void OnUsernameChanged(FixedString128Bytes oldName, FixedString128Bytes newName)
    {
        _uiUsername.text = newName.ConvertToString();
    }

    private void OnMasterclientChanged(bool oldVal, bool newVal)
    {
        _uiMasterIcon.enabled = newVal;
    }

    public void SetColor()
    {
        if (!_localPlayerState) return;

        _localPlayerState.Color = Random.ColorHSV();
        SetColorServerRpc(_localPlayerState.Color);
    }

    private void UpdateData()
    {
        SetStateServerRpc(_localPlayerState.Color, _localPlayerState.Username);
    }

    [ServerRpc]
    private void SetStateServerRpc(Color color_, string username_)
    {
        _nvColor.Value = color_;
        _nvUsername.Value = username_;
    }

    [ServerRpc]
    private void SetColorServerRpc(Color color_)
    {
        _nvColor.Value = color_;
    }

    [ServerRpc]
    private void SetMasterServerRpc(bool masterclient_)
    {
        _nvHostFlag.Value = masterclient_;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner && _localPlayerState)
        {
            Debug.Log($"update state in server, _uiUsername {_localPlayerState.Username}");
            SetStateServerRpc(_localPlayerState.Color, _localPlayerState.Username);
            SetMasterServerRpc(IsHost);
        }
    }
}
