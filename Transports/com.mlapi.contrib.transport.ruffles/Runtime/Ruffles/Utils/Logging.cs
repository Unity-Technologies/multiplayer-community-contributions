using System;
using System.Reflection;

namespace Ruffles.Utils
{
    /// <summary>
    /// Logging utils.
    /// </summary>
    public static class Logging
    {
        /// <summary>
        /// Whether or not to try to auto hook common loggers such as UnityEngine.Debug
        /// </summary>
        public static bool TryAutoHookCommonLoggers = true;
        
        /// <summary>
        /// Occurs when ruffles spits out an info log.
        /// </summary>
        public static event Action<string> OnInfoLog = (value) => Console.WriteLine("[INFO] " + value);
        /// <summary>
        /// Occurs when ruffles spits out a warning log.
        /// </summary>
        public static event Action<string> OnWarningLog = (value) => Console.WriteLine("[WARNING] " + value);
        /// <summary>
        /// Occurs when ruffles spits out an error log.
        /// </summary>
        public static event Action<string> OnErrorLog = (value) => Console.WriteLine("[ERROR] " + value);

        /// <summary>
        /// Gets the current log level.
        /// </summary>
        /// <value>The current log level.</value>
        public static LogLevel CurrentLogLevel = LogLevel.Info;

        internal static void LogInfo(string value)
        {
            if (OnInfoLog != null)
            {
                OnInfoLog(value);
            }
        }

        internal static void LogWarning(string value)
        {
            if (OnWarningLog != null)
            {
                OnWarningLog(value);
            }
        }

        internal static void LogError(string value)
        {
            if (OnErrorLog != null)
            {
                OnErrorLog(value);
            }
        }

        static Logging()
        {
            if (TryAutoHookCommonLoggers)
            {
                try
                {
                    Type unityDebugType = Type.GetType("UnityEngine.Debug, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");

                    if (unityDebugType != null)
                    {
                        MethodInfo infoLogMethod = unityDebugType.GetMethod("Log", BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(object) }, null);
                        MethodInfo warningLogMethod = unityDebugType.GetMethod("LogWarning", BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(object) }, null);
                        MethodInfo errorLogMethod = unityDebugType.GetMethod("LogError", BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(object) }, null);

                        if (infoLogMethod != null)
                        {
                            OnInfoLog += (value) =>
                            {
                                infoLogMethod.Invoke(null, new object[] { value });
                            };

                            if (CurrentLogLevel <= LogLevel.Debug) LogInfo("UnityEngine.Debug.Log(object) was hooked");
                        }

                        if (warningLogMethod != null)
                        {
                            OnWarningLog += (value) =>
                            {
                                warningLogMethod.Invoke(null, new object[] { value });
                            };

                            if (CurrentLogLevel <= LogLevel.Debug) LogInfo("UnityEngine.Debug.LogWarning(object) was hooked");
                        }

                        if (errorLogMethod != null)
                        {
                            OnErrorLog += (value) =>
                            {
                                errorLogMethod.Invoke(null, new object[] { value });
                            };

                            if (CurrentLogLevel <= LogLevel.Debug) LogInfo("UnityEngine.Debug.LogError(object) was hooked");
                        }
                    }
                }
                catch (TypeLoadException)
                {
                    if (CurrentLogLevel <= LogLevel.Debug) LogInfo("Could not load custom logging hook from UnityEngine.Debug");
                }
            }
        }
    }

    /// <summary>
    /// Log level
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// Detailed steps of every event.
        /// </summary>
        Debug,
        /// <summary>
        /// General events such as when a client connects.
        /// </summary>
        Info,
        /// <summary>
        /// A potential problem has occured. It doesnt prevent us from continuing. This occurs for things that might be others fault, such as invalid configurations.
        /// </summary>
        Warning,
        /// <summary>
        /// An error that affects us occured. Usually means the fault of us.
        /// </summary>
        Error,
        /// <summary>
        /// Logs nothing.
        /// </summary>
        Nothing
    }
}
