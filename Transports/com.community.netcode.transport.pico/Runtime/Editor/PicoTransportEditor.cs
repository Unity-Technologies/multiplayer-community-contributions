#if UNITY_EDITOR
using UnityEditor;
using Netcode.Transports.Pico;

namespace Netcode.Transport.Pico
{
    [CustomEditor(typeof(PicoTransport))]
    [CanEditMultipleObjects]
    public class PicoTransportEditor : Editor
    {
        SerializedProperty _workMode;
        SerializedProperty _independentInfo;
        SerializedProperty _transportLogLevel;
        void OnEnable()
        {
            _workMode = serializedObject.FindProperty("WorkMode");
            _independentInfo = serializedObject.FindProperty("SimpleModeInfo");
            _transportLogLevel = serializedObject.FindProperty("TransportLogLevel");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(_workMode);
            EditorGUILayout.PropertyField(_transportLogLevel);
            PicoTransport.SetLogLevel((PicoTransport.LogLevel)_transportLogLevel.enumValueIndex);
            PicoTransport.EWorkMode tmpWorkMode = (PicoTransport.EWorkMode)_workMode.enumValueIndex;
            switch (tmpWorkMode)
            {
                case PicoTransport.EWorkMode.ExternalRoom:
                    {
                        EditorGUILayout.LabelField("ExternalRoom Mode");
                    }
                    break;
                case PicoTransport.EWorkMode.Simple:
                    {
                        EditorGUILayout.LabelField("Simple Mode");
                        EditorGUILayout.PropertyField(_independentInfo, true);
                    }
                    break;
                default:
                    EditorGUILayout.LabelField("invalid work mode");
                    return;
            }
            serializedObject.ApplyModifiedProperties();
        } //OnInspectorGUI()

    }
}
#endif
