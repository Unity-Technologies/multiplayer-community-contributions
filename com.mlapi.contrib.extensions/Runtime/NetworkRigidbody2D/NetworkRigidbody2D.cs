using MLAPI;
using MLAPI.NetworkVariable;
using UnityEngine;

public class NetworkRigidbody2D : NetworkBehaviour
{
    #region Variables

    [Header("Settings")]
    public Rigidbody2D target;

    [Header("Velocity")]
    public bool syncVelocity = true;

    public float syncVelocitySensitivity = 0.1f;

    [Header("Angular Velocity")]
    public bool syncAngularVelocity = true;

    public float syncAngularVelocitySensitivity = 0.1f;

    #endregion Variables

    #region Synced Variables

    private readonly NetworkVariableVector2 velocity = new NetworkVariableVector2(new NetworkVariableSettings() { WritePermission = NetworkVariablePermission.Everyone });
    private readonly NetworkVariableFloat angularVelocity = new NetworkVariableFloat(new NetworkVariableSettings() { WritePermission = NetworkVariablePermission.Everyone });

    #endregion Synced Variables

    #region Accessors

    public Vector2 Velocity
    {
        get => velocity.Value;
        set
        {
            // Only set new velocity value if it changed more then syncVelocitySensitivity
            if (syncVelocity && Vector2.SqrMagnitude(velocity.Value - value) > syncVelocitySensitivity * syncVelocitySensitivity)
            {
                target.velocity = value;
                velocity.Value = value;
            }
        }
    }

    public float AngularVelocity
    {
        get => angularVelocity.Value;
        set
        {
            // Onlyk set a new angular velocity value if it chnaged more than syncAngluarVelocitySensitivity
            if (syncAngularVelocity && Mathf.Abs(angularVelocity.Value - value) > syncAngularVelocitySensitivity)
            {
                target.angularVelocity = value;
                angularVelocity.Value = value;
            }
        }
    }

    #endregion Accessors

    #region Monobehaviour Methods

    private void OnValidate()
    {
        if (target == null)
        {
            target = GetComponent<Rigidbody2D>();
        }
    }

    private void FixedUpdate()
    {
        if (IsServer)
        {
            Velocity = target.velocity;
            AngularVelocity = target.angularVelocity;
        }
    }

    #endregion Monobehaviour Methods
}