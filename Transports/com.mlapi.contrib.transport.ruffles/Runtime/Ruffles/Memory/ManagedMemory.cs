using System;
#if DEBUG
using System.Diagnostics;
#endif
using Ruffles.Utils;

namespace Ruffles.Memory
{
    // Managed memory base class
    internal abstract class ManagedMemory
    {
        internal abstract string LeakedType { get; }
        internal abstract string LeakedData { get; }
        internal bool IsDead;
        internal bool ReleasedToGC;

#if DEBUG
        internal StackTrace allocStacktrace;
#endif

        ~ManagedMemory()
        {
            if (ReleasedToGC)
            {
                return;
            }

            try
            {
                // If shutdown of the CLR has started, or the application domain is being unloaded. We don't want to print leak warnings. As these are legitimate deallocs and not leaks.
                if (!Environment.HasShutdownStarted)
                {
                    if (!IsDead)
                    {
#if DEBUG
                        if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning(LeakedType + " was just leaked from the MemoryManager " + LeakedData + " AllocStack: " + allocStacktrace);
#else
                        if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning(LeakedType + " was just leaked from the MemoryManager " + LeakedData);
#endif
                    }
                    else
                    {
#if DEBUG
                        if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Dead " + LeakedType + " was just leaked from the MemoryManager " + LeakedData + " AllocStack: " + allocStacktrace);
#else
                        if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Dead " + LeakedType + " was just leaked from the MemoryManager " + LeakedData);
#endif
                    }
                }
            }
            catch
            {
                // Supress
            }
        }
    }
}
