using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class LogToUI : MonoBehaviour
{
    const int MAX_CONSOLE_LINES = 13;
    public DebuggingPanel DebuggingPanel;
    private static Queue<string> _consoleLines = new Queue<string>(MAX_CONSOLE_LINES);
    private static Dictionary<LogType, string> _logTypeStrs = new Dictionary<LogType, string>
    {
        { LogType.Error,     "ERR| " },
        { LogType.Assert,    "AST| " },
        { LogType.Warning,   "WAR| " },
        { LogType.Log,       "DBG| " },
        { LogType.Exception, "EXC| " }
    };
    private StringBuilder _strBuilder = new StringBuilder();

    private void Awake()
    {
        if (_consoleLines.Count == 0)
        {
            for (int i = 0; i < MAX_CONSOLE_LINES; ++i)
            {
                _consoleLines.Enqueue("");
            }
        }

        Application.logMessageReceived += LogCallback;
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= LogCallback;
    }

    public void SetDebuggingPanel(DebuggingPanel debuggingPanel_)
    {
        DebuggingPanel = debuggingPanel_;
        AddLogToConsole("debug panel set", LogType.Log);
    }

    public void SetMatchInfo(string matchInfo)
    {
        DebuggingPanel.SetMatchInfo(matchInfo);
    }

    private void AddLogToConsole(string message, LogType type)
    {
        message = message.Trim();
        message = message.Trim('\n');
        message = _logTypeStrs[type] + message;
        if (_consoleLines.Count == MAX_CONSOLE_LINES)
            _consoleLines.Dequeue();
        _consoleLines.Enqueue(message);

        if (!DebuggingPanel) return;
        _strBuilder.Clear();
        foreach (string line in _consoleLines)
        {
            _strBuilder.Append(line + "\n");
        }
        DebuggingPanel.SetDebugLog(_strBuilder.ToString());
    }

    private void LogCallback(string condition, string stackTrace, LogType type)
    {
        AddLogToConsole(condition, type);
    }
}
