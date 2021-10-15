using System;
using Ruffles.Collections;
using Ruffles.Configuration;
using Ruffles.Connections;
using Ruffles.Memory;
using Ruffles.Messaging;
using Ruffles.Time;
using Ruffles.Utils;
using Ruffles.Channeling.Channels.Shared;

namespace Ruffles.Channeling.Channels
{
    internal class ReliableOrderedChannel : IChannel
    {
        // Incoming sequencing
        private ushort _incomingLowestAckedSequence;
        private readonly SlidingWindow<NetTime> _lastAckTimes;
        private readonly object _receiveLock = new object();

        // Outgoing sequencing
        private ushort _lastOutgoingSequence;
        private PendingOutgoingPacketSequence? _lastOutgoingPacket;
        private readonly object _sendLock = new object();

        // Channel info
        private byte channelId;
        private Connection connection;
        private SocketConfig config;
        private MemoryManager memoryManager;

        internal ReliableOrderedChannel(byte channelId, Connection connection, SocketConfig config, MemoryManager memoryManager)
        {
            this.channelId = channelId;
            this.connection = connection;
            this.config = config;
            this.memoryManager = memoryManager;

            _lastOutgoingPacket = null;

            _lastAckTimes = new SlidingWindow<NetTime>(config.ReliableAckFlowWindowSize);
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
                _lastOutgoingSequence++;

                // Allocate the memory
                HeapMemory memory = memoryManager.AllocHeapMemory((uint)payload.Count + 4);

                // Write headers
                memory.Buffer[0] = HeaderPacker.Pack(MessageType.Data);
                memory.Buffer[1] = channelId;

                // Write the sequence
                memory.Buffer[2] = (byte)_lastOutgoingSequence;
                memory.Buffer[3] = (byte)(_lastOutgoingSequence >> 8);

                // Copy the payload
                Buffer.BlockCopy(payload.Array, payload.Offset, memory.Buffer, 4, payload.Count);

                if (_lastOutgoingPacket != null)
                {
                    // Dealloc the last packet
                    _lastOutgoingPacket.Value.DeAlloc(memoryManager);
                }

                // Add the memory to pending
                _lastOutgoingPacket = new PendingOutgoingPacketSequence()
                {
                    Sequence = _lastOutgoingSequence,
                    Attempts = 1,
                    LastSent = NetTime.Now,
                    FirstSent = NetTime.Now,
                    Memory = memory,
                    NotificationKey = notificationKey
                };

                // Allocate pointers
                HeapPointers pointers = memoryManager.AllocHeapPointers(1);

                // Point the first pointer to the memory
                pointers.Pointers[0] = memory;

                // Send the message to the router. Tell the router to NOT dealloc the memory as the channel needs it for resend purposes.
                ChannelRouter.SendMessage(pointers, false, connection, noMerge, memoryManager);
            }
        }

        public void HandleAck(ArraySegment<byte> payload)
        {
            // Read the sequence number
            ushort sequence = (ushort)(payload.Array[payload.Offset] | (ushort)(payload.Array[payload.Offset + 1] << 8));

            lock (_sendLock)
            {
                if (_lastOutgoingPacket != null && _lastOutgoingPacket.Value.Sequence == sequence)
                {
                    // Notify the user that we got an ack
                    ChannelRouter.HandlePacketAckedByRemote(connection, channelId, _lastOutgoingPacket.Value.NotificationKey);

                    // Dealloc the memory held by the last packet
                    _lastOutgoingPacket.Value.DeAlloc(memoryManager);

                    // TODO: Remove roundtripping from channeled packets and make specific ping-pong packets

                    // Get the roundtrp
                    ulong roundtrip = (ulong)Math.Round((NetTime.Now - _lastOutgoingPacket.Value.FirstSent).TotalMilliseconds);

                    // Report to the connection
                    connection.AddRoundtripSample(roundtrip);

                    // Kill the packet
                    _lastOutgoingPacket = null;
                }
            }
        }

        public HeapPointers HandleIncomingMessagePoll(ArraySegment<byte> payload)
        {
            // Read the sequence number
            ushort sequence = (ushort)(payload.Array[payload.Offset] | (ushort)(payload.Array[payload.Offset + 1] << 8));

            lock (_receiveLock)
            {
                if (SequencingUtils.Distance(sequence, _incomingLowestAckedSequence, sizeof(ushort)) <= 0)
                {
                    // We have already acked this message. Ack again

                    SendAck(sequence);

                    return null;
                }
                else
                {
                    // This is a future packet

                    // Add to sequencer
                    _incomingLowestAckedSequence = sequence;

                    // Send ack
                    SendAck(sequence);

                    // Alloc pointers
                    HeapPointers pointers = memoryManager.AllocHeapPointers(1);

                    // Alloc a memory wrapper
                    pointers.Pointers[0] = memoryManager.AllocMemoryWrapper(new ArraySegment<byte>(payload.Array, payload.Offset + 2, payload.Count - 2));

                    return pointers;
                }
            }
        }

        public void InternalUpdate(out bool timeout)
        {
            lock (_sendLock)
            {
                if (_lastOutgoingPacket != null)
                {
                    if ((NetTime.Now - _lastOutgoingPacket.Value.LastSent).TotalMilliseconds > connection.SmoothRoundtrip * config.ReliabilityResendRoundtripMultiplier && (NetTime.Now - _lastOutgoingPacket.Value.LastSent).TotalMilliseconds > config.ReliabilityMinPacketResendDelay)
                    {
                        if (_lastOutgoingPacket.Value.Attempts >= config.ReliabilityMaxResendAttempts)
                        {
                            // If they don't ack the message, disconnect them
                            timeout = true;
                            return;
                        }

                        _lastOutgoingPacket = new PendingOutgoingPacketSequence()
                        {
                            Attempts = (ushort)(_lastOutgoingPacket.Value.Attempts + 1),
                            LastSent = NetTime.Now,
                            FirstSent = _lastOutgoingPacket.Value.FirstSent,
                            Memory = _lastOutgoingPacket.Value.Memory,
                            Sequence = _lastOutgoingPacket.Value.Sequence,
                            NotificationKey = _lastOutgoingPacket.Value.NotificationKey
                        };

                        connection.SendInternal(new ArraySegment<byte>(_lastOutgoingPacket.Value.Memory.Buffer, (int)_lastOutgoingPacket.Value.Memory.VirtualOffset, (int)_lastOutgoingPacket.Value.Memory.VirtualCount), false);
                    }
                }
            }

            timeout = false;
        }

        private void SendAck(ushort sequence)
        {
            // Check the last ack time
            if (!_lastAckTimes.TryGet(sequence, out NetTime value) || ((NetTime.Now - value).TotalMilliseconds > connection.SmoothRoundtrip * config.ReliabilityResendRoundtripMultiplier && (NetTime.Now - value).TotalMilliseconds > config.ReliabilityMinAckResendDelay))
            {
                // Set the last ack time
                _lastAckTimes.Set(sequence, NetTime.Now);

                // Alloc ack memory
                HeapMemory ackMemory = memoryManager.AllocHeapMemory(4);

                // Write header
                ackMemory.Buffer[0] = HeaderPacker.Pack(MessageType.Ack);
                ackMemory.Buffer[1] = (byte)channelId;

                // Write sequence
                ackMemory.Buffer[2] = (byte)sequence;
                ackMemory.Buffer[3] = (byte)(sequence >> 8);

                // Send ack
                connection.SendInternal(new ArraySegment<byte>(ackMemory.Buffer, 0, 4), false);

                // Return memory
                memoryManager.DeAlloc(ackMemory);
            }
        }

        public void Release()
        {
            lock (_sendLock)
            {
                lock (_receiveLock)
                {
                    // Clear all incoming states
                    _incomingLowestAckedSequence = 0;

                    // Clear all outgoing states
                    _lastOutgoingSequence = 0;

                    // Reset the outgoing packet
                    if (_lastOutgoingPacket != null)
                    {
                        // Dealloc the memory
                        _lastOutgoingPacket.Value.DeAlloc(memoryManager);
                    }

                    // Release the packet
                    _lastOutgoingPacket = null;
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
