using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Netcode.Transports.Pico
{
    public partial class PicoTransport
    {
        public LogLevel TransportLogLevel;
        static private LogLevel _logLevel;

        public enum LogLevel
        {
            Debug = 0,
            Info,
            Warn,
            Error,
            Fatal
        };

        public static void SetLogLevel(LogLevel logLevel)
        {
            _logLevel = logLevel;
        }

        public static void PicoTransportLog(LogLevel level, object logCont)
        {
            if (level < _logLevel)
            {
                return;
            }
            switch (level)
            {
                case LogLevel.Debug:
                    LogInfo(logCont);
                    break;
                case LogLevel.Info:
                    LogInfo(logCont);
                    break;
                case LogLevel.Warn:
                    LogWarning(logCont);
                    break;
                case LogLevel.Error:
                    LogError(logCont);
                    break;
                case LogLevel.Fatal:
                    LogError(logCont);
                    break;
                default:
                    break;
            }
            return;
        }

        private static void LogInfo(object logCont)
        {
            var curtime = DateTime.Now.ToString("hh.mm.ss.ffffff");
            Debug.Log($"[{curtime}]PicoLibLog: {logCont}");
        }

        private static void LogWarning(object logCont)
        {
            var curtime = DateTime.Now.ToString("hh.mm.ss.ffffff");
            Debug.LogWarning($"[{curtime}]PicoLibLog: {logCont}");
        }

        private static void LogError(object logCont)
        {
            var curtime = DateTime.Now.ToString("hh.mm.ss.ffffff");
            Debug.LogError($"[{curtime}]PicoLibLog: {logCont}");
        }
    }
}
