using System;

namespace Ruffles.Channeling
{
    /// <summary>
    /// Enum representing different delivery methods.
    /// </summary>
    public enum ChannelType : byte
    {
        /// <summary>
        /// All messages are guaranteed to be delivered, the order is not guaranteed. 
        /// Duplicate packets are dropped.
        /// </summary>
        Reliable = 0,
        /// <summary>
        /// Messages are not guaranteed to be delivered, the order is not guaranteed.
        /// Duplicate packets are dropped.
        /// </summary>
        Unreliable = 1,
        /// <summary>
        /// Messages are not guaranteed to be delivered, the order is guaranteed.
        /// Older packets and duplicate packets are dropped.
        /// </summary>
        UnreliableOrdered = 2,
        /// <summary>
        /// All messages are guaranteed to be delivered, the order is guaranteed. 
        /// Duplicate packets are dropped.
        /// </summary>
        ReliableSequenced = 3,
        /// <summary>
        /// Messages are not guaranteed to be delivered, the order is not guaranteed.
        /// Duplicate packets are not dropped.
        /// </summary>
        UnreliableRaw = 4,
        /// <summary>
        /// Messages are guaranteed to be delivered, the order is guaranteed.
        /// Messages can be of a size larger than the MTU.
        /// Duplicate packets are dropped
        /// </summary>
        ReliableSequencedFragmented = 5,
        /// <summary>
        /// All messages are not guaranteed to be delivered, the order is guaranteed.
        /// If sending multiple messages, at least one message is guaranteed to be delivered.
        /// Duplicate packets are dropped
        /// </summary>
        ReliableOrdered = 6,
        /// <summary>
        /// All messages are guaranteed to be delivered, the order is not guaranteed.
        /// Messages can be of a size larger than the MTU.
        /// Duplicate packets are dropped.
        /// </summary>
        ReliableFragmented = 7
    }
}
