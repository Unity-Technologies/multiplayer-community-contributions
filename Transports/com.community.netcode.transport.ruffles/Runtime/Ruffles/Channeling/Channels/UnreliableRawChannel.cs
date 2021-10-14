using System;
using Ruffles.Configuration;
using Ruffles.Connections;
using Ruffles.Memory;
using Ruffles.Messaging;
using Ruffles.Utils;

namespace Ruffles.Channeling.Channels
{
    internal class UnreliableRawChannel : IChannel
    {
        // Channel info
        private byte channelId;
        private Connection connection;
        private SocketConfig config;
        private MemoryManager memoryManager;

        internal UnreliableRawChannel(byte channelId, Connection connection, SocketConfig config, MemoryManager memoryManager)
        {
            this.channelId = channelId;
            this.connection = connection;
            this.config = config;
            this.memoryManager = memoryManager;
        }

        public void CreateOutgoingMessage(ArraySegment<byte> payload, bool noMerge, ulong notificationKey)
        {
            if (payload.Count > connection.MTU)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Tried to send message that was too large. Use a fragmented channel instead. [Size=" + payload.Count + "] [MaxMessageSize=" + config.MaxFragments + "]");
                return;
            }

            // Allocate the memory
            HeapMemory memory = memoryManager.AllocHeapMemory((uint)payload.Count + 2);

            // Write headers
            memory.Buffer[0] = HeaderPacker.Pack(MessageType.Data);
            memory.Buffer[1] = channelId;

            // Copy the payload
            Buffer.BlockCopy(payload.Array, payload.Offset, memory.Buffer, 2, payload.Count);

            // Allocate pointers
            HeapPointers pointers = memoryManager.AllocHeapPointers(1);

            // Point the first pointer to the memory
            pointers.Pointers[0] = memory;

            // Send the message to the router. Tell the router to dealloc the memory as the channel no longer needs it.
            ChannelRouter.SendMessage(pointers, true, connection, noMerge, memoryManager);
        }

        public void HandleAck(ArraySegment<byte> payload)
        {
            // Unreliable messages have no acks.
        }

        public HeapPointers HandleIncomingMessagePoll(ArraySegment<byte> payload)
        {
            // Alloc pointers
            HeapPointers pointers = memoryManager.AllocHeapPointers(1);

            // Alloc wrapper
            pointers.Pointers[0] = memoryManager.AllocMemoryWrapper(new ArraySegment<byte>(payload.Array, payload.Offset, payload.Count));

            return pointers;
        }

        public void InternalUpdate(out bool timeout)
        {
            // Unreliable doesnt need to resend, thus no internal loop is required
            timeout = false;
        }

        public void Release()
        {
            // UnreliableRaw has nothing to clean up
        }

        public void Assign(byte channelId, Connection connection, SocketConfig config, MemoryManager memoryManager)
        {
            this.channelId = channelId;
            this.connection = connection;
            this.config = config;
            this.memoryManager = memoryManager;
        }
    }
}
