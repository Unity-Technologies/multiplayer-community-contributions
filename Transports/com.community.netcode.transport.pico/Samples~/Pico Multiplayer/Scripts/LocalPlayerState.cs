using UnityEngine;

public class LocalPlayerState : Singleton<LocalPlayerState>
{
    [HideInInspector]
    public Color Color;
    [HideInInspector]
    public string Username;

    public event System.Action OnSelfStateChange;

    public void Init(string selfName)
    {
        Color = Random.ColorHSV();
        Username = selfName;// message.Data.DisplayName;
        OnSelfStateChange?.Invoke();
    }

}
