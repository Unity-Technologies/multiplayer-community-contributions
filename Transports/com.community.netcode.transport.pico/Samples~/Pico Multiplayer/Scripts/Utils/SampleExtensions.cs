using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : Singleton<T>
{
    public static T Instance { get; private set; }

    protected void Awake()
    {
        Debug.Assert(Instance == null, $"Singleton {nameof(T)} has been instantiated more than once.");
        Instance = (T)this;
    }
}

public static class SampleExtensions
{
    public static bool IsCloseTo(this float a, float b, float epsilon = 0.0001f)
    {
        return Mathf.Abs(a - b) < epsilon;
    }
}
