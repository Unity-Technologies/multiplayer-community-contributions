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
    // TODO: Make single fragment messages faster
    // TODO: Remove array allocs
    internal class ReliableSequencedFragmentedChannel : IChannel
    {
        // Incoming sequencing
        private ushort _incomingLowestAckedSequence;
        private readonly HeapableFixedDictionary<PendingIncomingPacketFragmented> _receiveSequencer;
        private readonly object _receiveLock = new object();

        // Outgoing sequencing
        private ushort _lastOutgoingSequence;
        private ushort _outgoingLowestAckedSequence;
        private readonly HeapableFixedDictionary<PendingOutgoingPacketFragmented> _sendSequencer;
        private readonly Queue<PendingSend> _pendingSends = new Queue<PendingSend>();
        private readonly object _sendLock = new object();

        // Channel info
        private byte channelId;
        private Connection connection;
        private SocketConfig config;
        private MemoryManager memoryManager;

        internal ReliableSequencedFragmentedChannel(byte channelId, Connection connection, SocketConfig config, MemoryManager memoryManager)
        {
            this.channelId = channelId;
            this.connection = connection;
            this.config = config;
            this.memoryManager = memoryManager;

            // Alloc the in flight windows for receive and send
            _receiveSequencer = new HeapableFixedDictionary<PendingIncomingPacketFragmented>(config.ReliabilityWindowSize, memoryManager);
            _sendSequencer = new HeapableFixedDictionary<PendingOutgoingPacketFragmented>(config.ReliabilityWindowSize, memoryManager);
        }

        public HeapPointers HandleIncomingMessagePoll(ArraySegment<byte> payload)
        {
            // Read the sequence number
            ushort sequence = (ushort)(payload.Array[payload.Offset] | (ushort)(payload.Array[payload.Offset + 1] << 8));
            // Read the raw fragment data
            ushort encodedFragment = (ushort)(payload.Array[payload.Offset + 2] | (ushort)(payload.Array[payload.Offset + 3] << 8));
            // The fragmentId is the last 15 least significant bits
            ushort fragment = (ushort)(encodedFragment & 32767);
            // IsFinal is the most significant bit
            bool isFinal = (ushort)((encodedFragment & 32768) >> 15) == 1;

            if (fragment >= config.MaxFragments)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("FragmentId was too large. [FragmentId=" + fragment + "] [Config.MaxFragments=" + config.MaxFragments + "]. The fragment was silently dropped, expect a timeout.");
                return null;
            }

            lock (_receiveLock)
            {
                if (SequencingUtils.Distance(sequence, _incomingLowestAckedSequence, sizeof(ushort)) <= 0 ||
                    ((_receiveSequencer.TryGet(sequence, out PendingIncomingPacketFragmented value) && 
                    (value.IsComplete || (value.Fragments != null && value.Fragments.VirtualCount > fragment && value.Fragments.Pointers[value.Fragments.VirtualOffset + fragment] != null)))))
                {
                    // We have already acked this message. Ack again

                    SendAckEncoded(sequence, encodedFragment);

                    return null;
                }
                else
                {
                    // This is a packet after the last. One that is not yet completed

                    if (!_receiveSequencer.CanUpdateOrSet(sequence))
                    {
                        // If we cant update or set, that means the window is full and we are not in the window.

                        if (Logging.CurrentLogLevel <= LogLevel.Warning) Logging.LogWarning("Incoming packet window is exhausted. Expect delays");
                        return null;
                    }

                    if (!_receiveSequencer.TryGet(sequence, out value))
                    {
                        // If this is the first fragment we ever get, index the data.

                        // TODO: Alloc more size and just expand later in the pointer array

                        HeapPointers fragmentPointers = memoryManager.AllocHeapPointers((uint)fragment + 1);

                        value = new PendingIncomingPacketFragmented()
                        {
                            Fragments = fragmentPointers,
                            Size = isFinal ? (ushort?)(fragment + 1) : null
                        };

                        _receiveSequencer.Set(sequence, value);
                    }
                    else
                    {
                        // If the first fragment we got was fragment 1 / 500. The fragments array will only be of size 128. We need to potentially resize it
                        if (value.Fragments.Pointers.Length - value.Fragments.VirtualOffset <= fragment)
                        {
                            // We need to expand the fragments array.

                            // Alloc new array
                            HeapPointers newPointers = memoryManager.AllocHeapPointers((uint)fragment + 1);

                            // Copy old values
                            Array.Copy(value.Fragments.Pointers, newPointers.Pointers, value.Fragments.Pointers.Length);

                            // Return the memory for the old
                            memoryManager.DeAlloc(value.Fragments);

                            // Update the index
                            value = new PendingIncomingPacketFragmented()
                            {
                                Fragments = newPointers,
                                Size = isFinal ? (ushort?)(fragment + 1) : value.Size
                            };

                            _receiveSequencer.Update(sequence, value);
                        }

                        // We might also have to expand the virtual count
                        if (value.Fragments.VirtualCount <= fragment)
                        {
                            // Update the new virtual count
                            value.Fragments.VirtualCount = (uint)fragment + 1;

                            // Update the struct to set the size if it has changed (TODO: Check if needed)
                            value = new PendingIncomingPacketFragmented()
                            {
                                Fragments = value.Fragments,
                                Size = isFinal ? (ushort?)(fragment + 1) : value.Size
                            };

                            _receiveSequencer.Update(sequence, value);
                        }
                    }

                    if (value.Fragments.Pointers[value.Fragments.VirtualOffset + fragment] == null)
                    {
                        // Alloc some memory for the fragment
                        HeapMemory memory = memoryManager.AllocHeapMemory((uint)payload.Count - 4);

                        // Copy the payload
                        Buffer.BlockCopy(payload.Array, payload.Offset + 4, memory.Buffer, 0, payload.Count - 4);

                        // Add fragment to index
                        value.Fragments.Pointers[value.Fragments.VirtualOffset + fragment] = memory;
                    }

                    // Send ack
                    SendAckEncoded(sequence, encodedFragment);

                    uint completedSequentialPackets = 0;

                    // Calculate the amount of sequential, ready to be delivered, packets
                    for (int i = 1; (_receiveSequencer.TryGet((ushort)(_incomingLowestAckedSequence + i), out value) && value.IsComplete); i++)
                    {
                        completedSequentialPackets++;
                    }

                    if (completedSequentialPackets > 0)
                    {
                        // Alloc pointers
                        HeapPointers pointers = memoryManager.AllocHeapPointers(completedSequentialPackets);

                        for (int i = 0; _receiveSequencer.TryGet((ushort)(_incomingLowestAckedSequence + 1), out value) && value.IsComplete; i++)
                        {
                            // Get the next packet that is not yet given to the user.
                            ++_incomingLowestAckedSequence;

                            // Get the total size of all fragments
                            uint totalSize = value.TotalByteSize;

                            // Alloc memory for that large segment
                            HeapMemory memory = memoryManager.AllocHeapMemory(totalSize);

                            // Keep track of where we are, fragments COULD have different sizes.
                            int bufferPosition = 0;

                            if (value.Fragments != null)
                            {
                                // Copy all the parts
                                for (int x = 0; x < value.Fragments.VirtualCount; x++)
                                {
                                    // Copy fragment to final buffer
                                    Buffer.BlockCopy(((HeapMemory)value.Fragments.Pointers[value.Fragments.VirtualOffset + x]).Buffer, (int)((HeapMemory)value.Fragments.Pointers[value.Fragments.VirtualOffset + x]).VirtualOffset, memory.Buffer, bufferPosition, (int)((HeapMemory)value.Fragments.Pointers[value.Fragments.VirtualOffset + x]).VirtualCount);

                                    bufferPosition += (int)((HeapMemory)value.Fragments.Pointers[value.Fragments.VirtualOffset + x]).VirtualCount;
                                }
                            }

                            // Free the memory of all the individual fragments
                            value.DeAlloc(memoryManager);

                            // Kill
                            _receiveSequencer.Remove(_incomingLowestAckedSequence);

                            pointers.Pointers[i] = memoryManager.AllocMemoryWrapper(memory);
                        }

                        return pointers;
                    }

                    return null;
                }
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
            // Calculate the amount of fragments required
            int fragmentsRequired = (payload.Count + (connection.MTU - 1)) / connection.MTU;

            if (fragmentsRequired >= config.MaxFragments)
            {
                if (Logging.CurrentLogLevel <= LogLevel.Error) Logging.LogError("Tried to create message that was too large. [Size=" + payload.Count + "] [FragmentsRequired=" + fragmentsRequired + "] [Config.MaxFragments=" + config.MaxFragments + "]");
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

                // Alloc array
                HeapPointers memoryParts = memoryManager.AllocHeapPointers((uint)fragmentsRequired);

                int position = 0;

                for (ushort i = 0; i < fragmentsRequired; i++)
                {
                    // Calculate message size
                    int messageSize = Math.Min(connection.MTU, payload.Count - position);

                    // Allocate memory for each fragment
                    memoryParts.Pointers[memoryParts.VirtualOffset + i] = memoryManager.AllocHeapMemory((uint)(messageSize + 6));

                    // Write headers
                    ((HeapMemory)memoryParts.Pointers[memoryParts.VirtualOffset + i]).Buffer[0] = HeaderPacker.Pack(MessageType.Data);
                    ((HeapMemory)memoryParts.Pointers[memoryParts.VirtualOffset + i]).Buffer[1] = channelId;

                    // Write the sequence
                    ((HeapMemory)memoryParts.Pointers[memoryParts.VirtualOffset + i]).Buffer[2] = (byte)_lastOutgoingSequence;
                    ((HeapMemory)memoryParts.Pointers[memoryParts.VirtualOffset + i]).Buffer[3] = (byte)(_lastOutgoingSequence >> 8);

                    // Write the fragment
                    ((HeapMemory)memoryParts.Pointers[memoryParts.VirtualOffset + i]).Buffer[4] = (byte)(i & 32767);
                    ((HeapMemory)memoryParts.Pointers[memoryParts.VirtualOffset + i]).Buffer[5] = (byte)(((i & 32767) >> 8) | (byte)(i == fragmentsRequired - 1 ? 128 : 0));

                    // Write the payload
                    Buffer.BlockCopy(payload.Array, payload.Offset + position, ((HeapMemory)memoryParts.Pointers[memoryParts.VirtualOffset + i]).Buffer, 6, messageSize);

                    // Increase the position
                    position += messageSize;
                }

                // Alloc outgoing fragment structs
                HeapPointers outgoingFragments = memoryManager.AllocHeapPointers((uint)fragmentsRequired);

                for (int i = 0; i < fragmentsRequired; i++)
                {
                    // Add the memory to the outgoing sequencer array
                    outgoingFragments.Pointers[outgoingFragments.VirtualOffset + i] = new PendingOutgoingFragment()
                    {
                        Attempts = 1,
                        LastSent = NetTime.Now,
                        FirstSent = NetTime.Now,
                        Memory = ((HeapMemory)memoryParts.Pointers[memoryParts.VirtualOffset + i])
                    };
                }

                // Add the memory to the outgoing sequencer
                _sendSequencer.Set(_lastOutgoingSequence, new PendingOutgoingPacketFragmented()
                {
                    Fragments = outgoingFragments,
                    NotificationKey = notificationKey
                });

                // Send the message to the router. Tell the router to NOT dealloc the memory as the channel needs it for resend purposes.
                ChannelRouter.SendMessage(memoryParts, false, connection, noMerge, memoryManager);
            }
        }

        public void HandleAck(ArraySegment<byte> payload)
        {
            // Read the sequence number
            ushort sequence = (ushort)(payload.Array[payload.Offset] | (ushort)(payload.Array[payload.Offset + 1] << 8));
            // Read the raw fragment data
            ushort encodedFragment = (ushort)(payload.Array[payload.Offset + 2] | (ushort)(payload.Array[payload.Offset + 3] << 8));
            // The fragmentId is the last 15 least significant bits
            ushort fragment = (ushort)(encodedFragment & 32767);
            // IsFinal is the most significant bit
            bool isFinal = (ushort)((encodedFragment & 32768) >> 15) == 1;

            lock (_sendLock)
            {
                if (_sendSequencer.TryGet(sequence, out PendingOutgoingPacketFragmented value) && value.Fragments.VirtualCount > fragment && value.Fragments.Pointers[fragment] != null)
                {
                    // Dealloc the memory held by the sequencer for the packet
                    ((PendingOutgoingFragment)value.Fragments.Pointers[fragment]).DeAlloc(memoryManager);

                    // TODO: Remove roundtripping from channeled packets and make specific ping-pong packets

                    // Get the roundtrp
                    ulong roundtrip = (ulong)Math.Round((NetTime.Now - ((PendingOutgoingFragment)value.Fragments.Pointers[fragment]).FirstSent).TotalMilliseconds);

                    // Report to the connection
                    connection.AddRoundtripSample(roundtrip);

                    // Kill the fragment packet
                    value.Fragments.Pointers[fragment] = null;

                    bool hasAllocatedAndAliveFragments = false;
                    for (int i = 0; i < value.Fragments.VirtualCount; i++)
                    {
                        if (value.Fragments.Pointers[i] != null)
                        {
                            hasAllocatedAndAliveFragments = true;
                            break;
                        }
                    }

                    if (!hasAllocatedAndAliveFragments)
                    {
                        // Notify user that the packet was acked
                        ChannelRouter.HandlePacketAckedByRemote(connection, channelId, value.NotificationKey);

                        // Dealloc the wrapper packet
                        value.DeAlloc(memoryManager);

                        // Kill the wrapper packet
                        _sendSequencer.Remove(sequence);

                        if (sequence == (ushort)(_outgoingLowestAckedSequence + 1))
                        {
                            // This was the next one.
                            _outgoingLowestAckedSequence++;
                        }
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

        private void SendAck(ushort sequence, ushort fragment, bool isFinal)
        {
            // Alloc ack memory
            HeapMemory ackMemory = memoryManager.AllocHeapMemory(4);

            // Write header
            ackMemory.Buffer[0] = HeaderPacker.Pack(MessageType.Ack);
            ackMemory.Buffer[1] = (byte)channelId;

            // Write sequence
            ackMemory.Buffer[2] = (byte)sequence;
            ackMemory.Buffer[3] = (byte)(sequence >> 8);

            // Write fragment
            ackMemory.Buffer[4] = (byte)(fragment & 32767);
            ackMemory.Buffer[5] = (byte)(((byte)((fragment & 32767) >> 8)) | (byte)(isFinal ? 128 : 0));

            // Send ack
            connection.SendInternal(new ArraySegment<byte>(ackMemory.Buffer, 0, 6), false);

            // Return memory
            memoryManager.DeAlloc(ackMemory);
        }

        private void SendAckEncoded(ushort sequence, ushort encodedFragment)
        {
            // Alloc ack memory
            HeapMemory ackMemory = memoryManager.AllocHeapMemory(4);

            // Write header
            ackMemory.Buffer[0] = HeaderPacker.Pack(MessageType.Ack);
            ackMemory.Buffer[1] = (byte)channelId;

            // Write sequence
            ackMemory.Buffer[2] = (byte)sequence;
            ackMemory.Buffer[3] = (byte)(sequence >> 8);

            // Write fragment
            ackMemory.Buffer[4] = (byte)encodedFragment;
            ackMemory.Buffer[5] = (byte)(encodedFragment >> 8);

            // Send ack
            connection.SendInternal(new ArraySegment<byte>(ackMemory.Buffer, 0, 6), false);

            // Return memory
            memoryManager.DeAlloc(ackMemory);
        }

        public void InternalUpdate(out bool timeout)
        {
            lock (_sendLock)
            {
                for (ushort i = (ushort)(_outgoingLowestAckedSequence + 1); SequencingUtils.Distance(i, _lastOutgoingSequence, sizeof(ushort)) <= 0; i++)
                {
                    if (_sendSequencer.TryGet(i, out PendingOutgoingPacketFragmented value))
                    {
                        for (int j = 0; j < value.Fragments.VirtualCount; j++)
                        {
                            if (value.Fragments.Pointers[j] != null)
                            {
                                PendingOutgoingFragment fragment = (PendingOutgoingFragment)value.Fragments.Pointers[j];

                                if ((NetTime.Now - fragment.LastSent).TotalMilliseconds > connection.SmoothRoundtrip * config.ReliabilityResendRoundtripMultiplier && (NetTime.Now - fragment.LastSent).TotalMilliseconds > config.ReliabilityMinPacketResendDelay)
                                {
                                    if (fragment.Attempts >= config.ReliabilityMaxResendAttempts)
                                    {
                                        // If they don't ack the message, disconnect them
                                        timeout = true;
                                        return;
                                    }

                                    fragment = new PendingOutgoingFragment()
                                    {
                                        Attempts = (ushort)(((PendingOutgoingFragment)value.Fragments.Pointers[j]).Attempts + 1),
                                        LastSent = NetTime.Now,
                                        FirstSent = ((PendingOutgoingFragment)value.Fragments.Pointers[j]).FirstSent,
                                        Memory = ((PendingOutgoingFragment)value.Fragments.Pointers[j]).Memory
                                    };

                                    value.Fragments.Pointers[j] = fragment;

                                    connection.SendInternal(new ArraySegment<byte>(fragment.Memory.Buffer, (int)fragment.Memory.VirtualOffset, (int)fragment.Memory.VirtualCount), false);
                                }
                            }
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
 