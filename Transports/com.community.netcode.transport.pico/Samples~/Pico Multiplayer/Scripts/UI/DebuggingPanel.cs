using UnityEngine;
using TMPro;

public class DebuggingPanel : MonoBehaviour
{
    const string MATCH_UI_NAME = "MatchedInfo";
    const string CONSOLE_LOG_UI_NAME = "ConsoleLog";
    private TMP_Text _consoleLog;
    private TMP_Text _matchedInfo;
    private LogToUI log;

    private void Awake()
    {
        TMP_Text[] candidates = GetComponentsInChildren<TMP_Text>();
        for (int i = 0; i < candidates.Length; ++i)
        {
            TMP_Text tmp = candidates[i];
            if (tmp.name == CONSOLE_LOG_UI_NAME)
            {
                _consoleLog = candidates[i];
            }
            if (tmp.name == MATCH_UI_NAME)
            {
                _matchedInfo = candidates[i];
            }
        }
    }

    private void Start()
    {
        log = FindObjectOfType<LogToUI>();
        log.SetDebuggingPanel(this);
    }

    public void SetDebugLog(string log)
    {
        _consoleLog.text = log;
    }

    public void SetMatchInfo(string matchInfo)
    {
        _matchedInfo.text = matchInfo;
    }
}
