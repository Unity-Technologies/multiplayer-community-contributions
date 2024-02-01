using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Netcode.Transports.Pico.Sample.PicoMultiplayer
{

    public class SampleApplication : MonoBehaviour
    {
        const string APP_PREFAB_PATH = "Prefabs/Application";

        public enum Scenes
        {
            Init,
            Start,
            Fight
        }

        public static Scenes CurrentScene;
        private bool _sceneLoaded = false;
        private ExternalModeSDKUser _picoUser;

        static SampleApplication _application;

        static public SampleApplication GetInstance()
        {
            if (!_application)
            {
                SampleApplication preExisted = FindObjectOfType<SampleApplication>();
                if (preExisted)
                {
                    _application = preExisted;
                }
                else
                {
                    Object prefab = Resources.Load(APP_PREFAB_PATH);
                    GameObject go = (GameObject)Instantiate(prefab, new Vector3(-4, 0, -12),
                        Quaternion.AngleAxis(45, Vector3.up));
                    _application = go.GetComponent<SampleApplication>();
                }

                _application.Refresh(true);
                DontDestroyOnLoad(_application.gameObject);
            }

            return _application;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            if (_picoUser)
            {
                _picoUser.OnStatusChange += HandlePicoStatusChange;
            }
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (_picoUser)
            {
                _picoUser.OnStatusChange -= HandlePicoStatusChange;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Debug.Log($"scene {scene.name} loaded");
            _sceneLoaded = true;
            CurrentScene = (Scenes)scene.buildIndex;
        }

        public void LoadScene(Scenes scene)
        {
            if (scene == CurrentScene) return;
            _sceneLoaded = false;
            SceneManager.LoadSceneAsync((int)scene);
        }

        private void Start()
        {
            _picoUser = GetComponent<ExternalModeSDKUser>();
            _picoUser.ServerCreatePlayerPrefabPath = "Prefabs/Player";
            _picoUser.OnStatusChange += HandlePicoStatusChange;
            StartCoroutine(Init());
        }

        public void Refresh(bool resetTransform)
        {
            Camera camera = FindObjectOfType<Camera>();
            Canvas[] canvas = GetComponentsInChildren<Canvas>();
            if (canvas.Length > 0)
            {
                canvas[0].worldCamera = camera;
            }

            if (resetTransform)
            {
                transform.SetPositionAndRotation(new Vector3(0, 0, -11), Quaternion.identity);
            }
        }

        private void HandlePicoStatusChange(ExternalModeSDKUser.EGameState oldState, ExternalModeSDKUser.EGameState newState,
            string inDesc, string inOpenID, string matchedInfo)
        {
            Debug.Log($"Application got new game state {newState}, {inOpenID}, {inDesc}");
            if (oldState != newState)
            {
                switch (newState)
                {
                    case ExternalModeSDKUser.EGameState.UserGot:
                        FindObjectOfType<LocalPlayerState>().Init(inOpenID);
                        break;
                    case ExternalModeSDKUser.EGameState.NotInited:
                        SwitchToStartScene();
                        break;
                    case ExternalModeSDKUser.EGameState.InRoom:
                        SwitchToFightScene();
                        break;
                    case ExternalModeSDKUser.EGameState.RoomLeft:
                        SwitchToStartScene();
                        break;
                }
            }
        }

        private IEnumerator Init()
        {
            LoadScene(Scenes.Start);
            yield return new WaitUntil(() => _sceneLoaded);
            Debug.Log("start scene loaded");
        }

        private IEnumerator SwitchScene(Scenes destination)
        {
            LoadScene(destination);
            yield return new WaitUntil(() => _sceneLoaded);

        }

        private IEnumerator WaitThenSwitchScene(int seconds, Scenes destination)
        {
            yield return new WaitForSeconds(seconds);
            LoadScene(destination);
            yield return new WaitUntil(() => _sceneLoaded);
        }

        public void OnPortalEnter()
        {
            _picoUser = GetComponent<ExternalModeSDKUser>();
            _picoUser.StartLeaveRoom();
        }

        public void SwitchToFightScene()
        {
            StartCoroutine(WaitThenSwitchScene(1, Scenes.Fight));
        }

        public void SwitchToStartScene()
        {
            StartCoroutine(SwitchScene(Scenes.Start));
        }

    }

}