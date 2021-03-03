using System;

namespace Ruffles.Channeling
{
    /// <summary>
    /// Enum representing different delivery methods.
    /// </summary>
    [Flags]
    public enum PooledChannelType : int
    {
        /// <summary>
        /// No channel.
        /// </summary>
        None = 0,
        /// <summary>
        /// Reliable channels.
        /// </summary>
        Reliable = 1,
        /// <summary>
        /// Unreliable channels.
        /// </summary>
        Unreliable = 2,
        /// <summary>
        /// UnreliableOrdered channels.
        /// </summary>
        UnreliableOrdered = 4,
        /// <summary>
        /// ReliableSequenced channels.
        /// </summary>
        ReliableSequenced = 8,
        /// <summary>
        /// UnreliableRaw channels.
        /// </summary>
        UnreliableRaw = 16,
        /// <summary>
        /// RelaibleSequencedFragmented channels.
        /// </summary>
        ReliableSequencedFragmented = 32,
        /// <summary>
        /// ReliableOrdered channels.
        /// </summary>
        ReliableOrdered = 64,
        /// <summary>
        /// ReliableFragmented channels.
        /// </summary>
        ReliableFragmented = 128,
        /// <summary>
        /// All channels.
        /// </summary>
        All = ~0
    }
}
