using System;
using Ruffles.Collections;
using Ruffles.Configuration;
using Ruffles.Connections;
using Ruffles.Memory;
using Ruffles.Messaging;
using Ruffles.Utils;

namespace Ruffles.Channeling.Channels
{
    internal class UnreliableChannel : IChannel
    {
        // Incoming sequencing
        private readonly SlidingWindow<bool> _incomingAckedPackets;
        private readonly object _receiveLock = new object();

        // Outgoing sequencing
        private ushort _lastOutboundSequenceNumber;
        private readonly object _sendLock = new object();

        // Channel info
        private byte channelId;
        private Connection connection;
        private SocketConfig config;
        private MemoryManager memoryManager;

        internal UnreliableChannel(byte channelId, Connection connection, SocketConfig config, MemoryManager memoryManager)
        {
            this.channelId = channelId;
            this.connection = connection;
            this.config = config;
            this.memoryManager = memoryManager;

            _incomingAckedPackets = new SlidingWindow<bool>(config.ReliabilityWindowSize);
        }

        public void CreateOutgoingMessage(ArraySegment<byte> payload, bool noMerge, ulong notificationKey)
        {
            if (payload.Count > connection.MTU)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Tried to send message that was too large. Use a fragmented channel instead. [Size=" + payload.Count + "] [MaxMessageSize=" + config.MaxFragments + "]");
                return;
            }

            lock (_sendLock)
            {
                // Increment the sequence number
                _lastOutboundSequenceNumber++;

                // Allocate the memory
                HeapMemory memory = memoryManager.AllocHeapMemory((uint)payload.Count + 4);

                // Write headers
                memory.Buffer[0] = HeaderPacker.Pack(MessageType.Data);
                memory.Buffer[1] = channelId;

                // Write the sequence
                memory.Buffer[2] = (byte)_lastOutboundSequenceNumber;
                memory.Buffer[3] = (byte)(_lastOutboundSequenceNumber >> 8);

                // Copy the payload
                Buffer.BlockCopy(payload.Array, payload.Offset, memory.Buffer, 4, payload.Count);

                // Allocate pointers
                HeapPointers pointers = memoryManager.AllocHeapPointers(1);

                // Point the first pointer to the memory
                pointers.Pointers[0] = memory;

                // Send the message to the router. Tell the router to dealloc the memory as the channel no longer needs it.
                ChannelRouter.SendMessage(pointers, true, connection, noMerge, memoryManager);
            }
        }

        public void HandleAck(ArraySegment<byte> payload)
        {
            // Unreliable messages have no acks.
        }

        public HeapPointers HandleIncomingMessagePoll(ArraySegment<byte> payload)
        {
            // Read the sequence number
            ushort sequence = (ushort)(payload.Array[payload.Offset] | (ushort)(payload.Array[payload.Offset + 1] << 8));

            lock (_receiveLock)
            {
                if (_incomingAckedPackets.Contains(sequence))
                {
                    // We have already received this message. Ignore it.
                    return null;
                }

                // Add to sequencer
                _incomingAckedPackets.Set(sequence, true);

                // Alloc pointers
                HeapPointers pointers = memoryManager.AllocHeapPointers(1);

                // Alloc wrapper
                pointers.Pointers[0] = memoryManager.AllocMemoryWrapper(new ArraySegment<byte>(payload.Array, payload.Offset + 2, payload.Count - 2));

                return pointers;
            }
        }

        public void InternalUpdate(out bool timeout)
        {
            // Unreliable doesnt need to resend, thus no internal loop is required
            timeout = false;
        }

        public void Release()
        {
            lock (_sendLock)
            {
                lock (_receiveLock)
                {
                    // Clear all outgoing states
                    _lastOutboundSequenceNumber = 0;
                }
            }
        }

        public void Assign(byte channelId, Connection connection, SocketConfig config, MemoryManager memoryManager)
        {
            lock (_sendLock)
            {
                lock (_receiveLock)
                {
                    this.channelId = channelId;
                    this.connection = connection;
                    this.config = config;
                    this.memoryManager = memoryManager;
                }
            }
        }
    }
}
