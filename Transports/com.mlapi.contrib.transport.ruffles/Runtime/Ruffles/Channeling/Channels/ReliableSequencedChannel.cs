using System;
using System.Collections.Generic;
using Ruffles.Channeling.Channels.Shared;
using Ruffles.Collections;
using Ruffles.Configuration;
using Ruffles.Connections;
using Ruffles.Memory;
using Ruffles.Messaging;
using Ruffles.Time;
using Ruffles.Utils;

namespace Ruffles.Channeling.Channels
{
    internal class ReliableSequencedChannel : IChannel
    {
        // Incoming sequencing
        private ushort _incomingLowestAckedSequence;
        private readonly HeapableFixedDictionary<PendingIncomingPacket> _receiveSequencer;
        private readonly SlidingWindow<NetTime> _lastAckTimes;
        private readonly object _receiveLock = new object();

        // Outgoing sequencing
        private ushort _lastOutgoingSequence;
        private ushort _outgoingLowestAckedSequence;
        private readonly HeapableFixedDictionary<PendingOutgoingPacket> _sendSequencer;
        private readonly Queue<PendingSend> _pendingSends = new Queue<PendingSend>();
        private readonly object _sendLock = new object();

        // Channel info
        private byte channelId;
        private Connection connection;
        private SocketConfig config;
        private MemoryManager memoryManager;

        internal ReliableSequencedChannel(byte channelId, Connection connection, SocketConfig config, MemoryManager memoryManager)
        {
            this.channelId = channelId;
            this.connection = connection;
            this.config = config;
            this.memoryManager = memoryManager;

            // Alloc the in flight windows for receive and send
            _receiveSequencer = new HeapableFixedDictionary<PendingIncomingPacket>(config.ReliabilityWindowSize, memoryManager);
            _sendSequencer = new HeapableFixedDictionary<PendingOutgoingPacket>(config.ReliabilityWindowSize, memoryManager);
            _lastAckTimes = new SlidingWindow<NetTime>(config.ReliableAckFlowWindowSize);
        }

        public HeapPointers HandleIncomingMessagePoll(ArraySegment<byte> payload)
        {
            // Read the sequence number
            ushort sequence = (ushort)(payload.Array[payload.Offset] | (ushort)(payload.Array[payload.Offset + 1] << 8));

            lock (_receiveLock)
            {
                if (SequencingUtils.Distance(sequence, _incomingLowestAckedSequence, sizeof(ushort)) <= 0 || _receiveSequencer.Contains(sequence))
                {
                    // We have already acked this message. Ack again

                    SendAck(sequence);

                    return null;
                }
                else if (sequence == (ushort)(_incomingLowestAckedSequence + 1))
                {
                    // This is the packet right after

                    _incomingLowestAckedSequence++;

                    // Send ack
                    SendAck(sequence);

                    uint additionalPackets = 0;

                    // Count all the additional sequential packets that are ready
                    for (int i = 1; (_receiveSequencer.TryGet((ushort)(_incomingLowestAckedSequence + i), out PendingIncomingPacket value)); i++)
                    {
                        additionalPackets++;
                    }

                    // Allocate pointers (alloc size 1, we might need more)
                    HeapPointers pointers = memoryManager.AllocHeapPointers(1 + additionalPackets);

                    // Point the first pointer to the memory that is known.
                    pointers.Pointers[0] = memoryManager.AllocMemoryWrapper(new ArraySegment<byte>(payload.Array, payload.Offset + 2, payload.Count - 2));

                    for (int i = 0; _receiveSequencer.TryGet((ushort)(_incomingLowestAckedSequence + 1), out PendingIncomingPacket value); i++)
                    {
                        // Update lowest incoming
                        ++_incomingLowestAckedSequence;

                        // Set the memory
                        pointers.Pointers[1 + i] = memoryManager.AllocMemoryWrapper(value.Memory);

                        // Kill
                        _receiveSequencer.Remove(_incomingLowestAckedSequence);
                    }

                    return pointers;

                }
                else if (SequencingUtils.Distance(sequence, _incomingLowestAckedSequence, sizeof(ushort)) > 0)
                {
                    // Future packet

                    if (!_receiveSequencer.CanUpdateOrSet(sequence))
                    {
                        // If we cant update or set, that means the window is full and we are not in the window.

                        if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Incoming packet window is exhausted. Expect delays");
                        return null;
                    }

                    if (!_receiveSequencer.Contains(sequence))
                    {
                        // Alloc payload plus header memory
                        HeapMemory memory = memoryManager.AllocHeapMemory((uint)payload.Count - 2);

                        // Copy the payload
                        Buffer.BlockCopy(payload.Array, payload.Offset + 2, memory.Buffer, 0, payload.Count - 2);

                        // Add to sequencer
                        _receiveSequencer.Set(sequence, new PendingIncomingPacket()
                        {
                            Memory = memory
                        });

                        // Send ack
                        SendAck(sequence);
                    }
                }

                return null;
            }
        }

        public void CreateOutgoingMessage(ArraySegment<byte> payload, bool noMerge, ulong notificationKey)
        {
            lock (_sendLock)
            {
                CreateOutgoingMessageInternal(payload, noMerge, notificationKey);
            }
        }

        private void CreateOutgoingMessageInternal(ArraySegment<byte> payload, bool noMerge, ulong notificationKey)
        {
            if (payload.Count > connection.MTU)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Tried to send message that was too large. Use a fragmented channel instead. [Size=" + payload.Count + "] [MaxMessageSize=" + config.MaxFragments + "]");
                return;
            }

            if (!_sendSequencer.CanSet((ushort)(_lastOutgoingSequence + 1)))
            {
                if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Outgoing packet window is exhausted. Expect delays");

                // Alloc memory
                HeapMemory memory = memoryManager.AllocHeapMemory((uint)payload.Count);

                // Copy the payload
                Buffer.BlockCopy(payload.Array, payload.Offset, memory.Buffer, 0, payload.Count);

                // Enqueue it
                _pendingSends.Enqueue(new PendingSend()
                {
                    Memory = memory,
                    NoMerge = noMerge,
                    NotificationKey = notificationKey
                });
            }
            else
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

                // Add the memory to the outgoing sequencer
                _sendSequencer.Set(_lastOutgoingSequence, new PendingOutgoingPacket()
                {
                    Attempts = 1,
                    LastSent = NetTime.Now,
                    FirstSent = NetTime.Now,
                    Memory = memory,
                    NotificationKey = notificationKey
                });

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

            // Handle the base ack
            HandleAck(sequence);

            if ((payload.Count - 2) > 0)
            {
                // There is more data. This has to be ack bits

                // Calculate the amount of ack bits
                int bits = (payload.Count - 2) * 8;

                // Iterate ack bits
                for (byte i = 0; i < bits; i++)
                {
                    // Get the ack for the current bit
                    bool isAcked = ((payload.Array[payload.Offset + 2 + (i / 8)] & ((byte)Math.Pow(2, (7 - (i % 8))))) >> (7 - (i % 8))) == 1;

                    if (isAcked)
                    {
                        // Handle the bit ack
                        HandleAck((ushort)(sequence - (i + 1)));
                    }
                }
            }
        }

        private void HandleAck(ushort sequence)
        {
            lock (_sendLock)
            {
                if (_sendSequencer.TryGet(sequence, out PendingOutgoingPacket value))
                {
                    // Send notification to user that the packet was acked
                    ChannelRouter.HandlePacketAckedByRemote(connection, channelId, value.NotificationKey);

                    // Dealloc the memory held by the sequencer for the packet
                    value.DeAlloc(memoryManager);

                    // TODO: Remove roundtripping from channeled packets and make specific ping-pong packets

                    // Get the roundtrp
                    ulong roundtrip = (ulong)Math.Round((NetTime.Now - value.FirstSent).TotalMilliseconds);

                    // Report to the connection
                    connection.AddRoundtripSample(roundtrip);

                    // Kill the packet
                    _sendSequencer.Remove(sequence);

                    if (sequence == (ushort)(_outgoingLowestAckedSequence + 1))
                    {
                        // This was the next one.
                        _outgoingLowestAckedSequence++;
                    }
                }

                // Loop from the lowest ack we got
                for (ushort i = _outgoingLowestAckedSequence; !_sendSequencer.Contains(i) && SequencingUtils.Distance(i, _lastOutgoingSequence, sizeof(ushort)) <= 0; i++)
                {
                    _outgoingLowestAckedSequence = i;
                }

                // Check if we can start draining pending pool
                while (_pendingSends.Count > 0 && _sendSequencer.CanSet((ushort)(_lastOutgoingSequence + 1)))
                {
                    // Dequeue the pending
                    PendingSend pending = _pendingSends.Dequeue();

                    // Sequence it
                    CreateOutgoingMessageInternal(new ArraySegment<byte>(pending.Memory.Buffer, (int)pending.Memory.VirtualOffset, (int)pending.Memory.VirtualCount), pending.NoMerge, pending.NotificationKey);

                    // Dealloc
                    memoryManager.DeAlloc(pending.Memory);
                }
            }
        }

        private void SendAck(ushort sequence)
        {
            // Check the last ack time
            if (!_lastAckTimes.TryGet(sequence, out NetTime value) || ((NetTime.Now - value).TotalMilliseconds > connection.SmoothRoundtrip * config.ReliabilityResendRoundtripMultiplier && (NetTime.Now - value).TotalMilliseconds > config.ReliabilityMinAckResendDelay))
            {
                // Set the last ack time
                _lastAckTimes.Set(sequence, NetTime.Now);

                // Alloc ack memory
                HeapMemory ackMemory = memoryManager.AllocHeapMemory(4 + (uint)(config.EnableMergedAcks ? config.MergedAckBytes : 0));

                // Write header
                ackMemory.Buffer[0] = HeaderPacker.Pack(MessageType.Ack);
                ackMemory.Buffer[1] = (byte)channelId;

                // Write sequence
                ackMemory.Buffer[2] = (byte)sequence;
                ackMemory.Buffer[3] = (byte)(sequence >> 8);

                if (config.EnableMergedAcks)
                {
                    // Reset the memory
                    for (int i = 0; i < config.MergedAckBytes; i++)
                    {
                        ackMemory.Buffer[4 + i] = 0;
                    }

                    // Set the bit fields
                    for (int i = 0; i < config.MergedAckBytes * 8; i++)
                    {
                        ushort bitSequence = (ushort)(sequence - (i + 1));
                        bool bitAcked = SequencingUtils.Distance(bitSequence, _incomingLowestAckedSequence, sizeof(ushort)) <= 0 || _receiveSequencer.Contains(bitSequence);

                        if (bitAcked)
                        {
                            // Set the ack time for this packet
                            _lastAckTimes.Set(bitSequence, NetTime.Now);
                        }

                        // Write single ack bit
                        ackMemory.Buffer[4 + (i / 8)] |= (byte)((bitAcked ? 1 : 0) << (7 - (i % 8)));
                    }
                }

                // Send ack
                connection.SendInternal(new ArraySegment<byte>(ackMemory.Buffer, 0, 4 + (config.EnableMergedAcks ? config.MergedAckBytes : 0)), false);

                // Return memory
                memoryManager.DeAlloc(ackMemory);
            }
        }

        public void InternalUpdate(out bool timeout)
        {
            lock (_sendLock)
            {
                for (ushort i = (ushort)(_outgoingLowestAckedSequence + 1); SequencingUtils.Distance(i, _lastOutgoingSequence, sizeof(ushort)) <= 0; i++)
                {
                    if (_sendSequencer.TryGet(i, out PendingOutgoingPacket value))
                    {
                        if ((NetTime.Now - value.LastSent).TotalMilliseconds > connection.SmoothRoundtrip * config.ReliabilityResendRoundtripMultiplier && (NetTime.Now - value.LastSent).TotalMilliseconds > config.ReliabilityMinPacketResendDelay)
                        {
                            if (value.Attempts >= config.ReliabilityMaxResendAttempts)
                            {
                                // If they don't ack the message, disconnect them
                                timeout = true;
                                return;
                            }

                            _sendSequencer.Update(i, new PendingOutgoingPacket()
                            {
                                Attempts = (ushort)(value.Attempts + 1),
                                LastSent = NetTime.Now,
                                FirstSent = value.FirstSent,
                                Memory = value.Memory,
                                NotificationKey = value.NotificationKey
                            });

                            connection.SendInternal(new ArraySegment<byte>(value.Memory.Buffer, (int)value.Memory.VirtualOffset, (int)value.Memory.VirtualCount), false);
                        }
                    }
                }
            }

            timeout = false;
        }

        public void Release()
        {
            lock (_sendLock)
            {
                lock (_receiveLock)
                {
                    // Clear all incoming states
                    _receiveSequencer.Release();
                    _incomingLowestAckedSequence = 0;

                    // Clear all outgoing states
                    _sendSequencer.Release();
                    _lastOutgoingSequence = 0;
                    _outgoingLowestAckedSequence = 0;

                    // Dealloc all pending
                    while (_pendingSends.Count > 0)
                    {
                        memoryManager.DeAlloc(_pendingSends.Dequeue().Memory);
                    }
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
 