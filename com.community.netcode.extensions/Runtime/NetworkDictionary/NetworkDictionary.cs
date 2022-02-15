using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Netcode
{
    /// <summary>
    /// Event based NetworkVariable container for syncing Dictionaries
    /// </summary>
    /// <typeparam name="TKey">The type for the dictionary keys</typeparam>
    /// <typeparam name="TValue">The type for the dictionary values</typeparam>
    public class NetworkDictionary<TKey, TValue> : NetworkVariableBase
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        private NativeHashMap<TKey, TValue> m_Dictionary = new NativeHashMap<TKey, TValue>(64, Allocator.Persistent);
        private NativeList<NetworkDictionaryEvent<TKey, TValue>> m_DirtyEvents = new NativeList<NetworkDictionaryEvent<TKey, TValue>>(64, Allocator.Persistent);

        /// <summary>
        /// Delegate type for dictionary changed event
        /// </summary>
        /// <param name="changeEvent">Struct containing information about the change event</param>
        public delegate void OnDictionaryChangedDelegate(NetworkDictionaryEvent<TKey, TValue> changeEvent);

        /// <summary>
        /// The callback to be invoked when the dictionary gets changed
        /// </summary>
        public event OnDictionaryChangedDelegate OnDictionaryChanged;

        /// <summary>
        /// Creates a NetworkDictionary with the default value and settings
        /// </summary>
        public NetworkDictionary() { }

        /// <summary>
        /// Creates a NetworkDictionary with the default value and custom settings
        /// </summary>
        /// <param name="readPerm">The read permission to use for the NetworkDictionary</param>
        /// <param name="values">The initial value to use for the NetworkDictionary</param>
        public NetworkDictionary(NetworkVariableReadPermission readPerm, IDictionary<TKey, TValue> values) : base(readPerm)
        {
            foreach (var pair in values)
            {
                m_Dictionary.Add(pair.Key, pair.Value);
            }
        }

        /// <summary>
        /// Creates a NetworkDictionary with a custom value and custom settings
        /// </summary>
        /// <param name="values">The initial value to use for the NetworkDictionary</param>
        public NetworkDictionary(IDictionary<TKey, TValue> values)
        {
            foreach (var pair in values)
            {
                m_Dictionary.Add(pair.Key, pair.Value);
            }
        }

        /// <inheritdoc />
        public override void ResetDirty()
        {
            base.ResetDirty();
            m_DirtyEvents.Clear();
        }

        /// <inheritdoc />
        public override bool IsDirty() => base.IsDirty() || m_DirtyEvents.Length > 0;

        /// <inheritdoc />
        public override void WriteDelta(FastBufferWriter writer)
        {
            if (base.IsDirty())
            {
                writer.WriteValueSafe((ushort)1);
                writer.WriteValueSafe(NetworkDictionaryEvent<TKey, TValue>.EventType.Full);
                WriteField(writer);

                return;
            }

            writer.WriteValueSafe((ushort)m_DirtyEvents.Length);

            for (int i = 0; i < m_DirtyEvents.Length; i++)
            {
                writer.WriteValueSafe(m_DirtyEvents[i].Type);

                switch (m_DirtyEvents[i].Type)
                {
                    case NetworkDictionaryEvent<TKey, TValue>.EventType.Add:
                        {
                            writer.WriteValueSafe(m_DirtyEvents[i].Key);
                            writer.WriteValueSafe(m_DirtyEvents[i].Value);
                        }
                        break;
                    case NetworkDictionaryEvent<TKey, TValue>.EventType.Remove:
                        {
                            writer.WriteValueSafe(m_DirtyEvents[i].Key);
                        }
                        break;
                    case NetworkDictionaryEvent<TKey, TValue>.EventType.Value:
                        {
                            writer.WriteValueSafe(m_DirtyEvents[i].Key);
                            writer.WriteValueSafe(m_DirtyEvents[i].Value);
                        }
                        break;
                    case NetworkDictionaryEvent<TKey, TValue>.EventType.Clear:
                        {
                        }
                        break;
                }
            }
        }

        /// <inheritdoc />
        public override void WriteField(FastBufferWriter writer)
        {
            writer.WriteValueSafe((ushort)m_Dictionary.Count());

            foreach (var pair in m_Dictionary)
            {
                writer.WriteValueSafe(pair.Key);
                writer.WriteValueSafe(pair.Value);
            }
        }

        /// <inheritdoc />
        public override void ReadField(FastBufferReader reader)
        {
            m_Dictionary.Clear();
            reader.ReadValueSafe(out ushort count);

            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out TKey key);
                reader.ReadValueSafe(out TValue value);
                m_Dictionary.Add(key, value);
            }
        }

        /// <inheritdoc />
        public override void ReadDelta(FastBufferReader reader, bool keepDirtyDelta)
        {
            reader.ReadValueSafe(out ushort deltaCount);

            for (int i = 0; i < deltaCount; i++)
            {
                reader.ReadValueSafe(out NetworkDictionaryEvent<TKey, TValue>.EventType eventType);

                switch (eventType)
                {
                    case NetworkDictionaryEvent<TKey, TValue>.EventType.Add:
                        {
                            reader.ReadValueSafe(out TKey key);
                            reader.ReadValueSafe(out TValue value);
                            m_Dictionary.Add(key, value);

                            OnDictionaryChanged?.Invoke(new NetworkDictionaryEvent<TKey, TValue>
                            {
                                Type = eventType,
                                Key = key,
                                Value = value
                            });

                            if (keepDirtyDelta)
                            {
                                m_DirtyEvents.Add(new NetworkDictionaryEvent<TKey, TValue>()
                                {
                                    Type = eventType,
                                    Key = key,
                                    Value = value
                                });
                            }
                        }
                        break;
                    case NetworkDictionaryEvent<TKey, TValue>.EventType.Remove:
                        {
                            reader.ReadValueSafe(out TKey key);
                            m_Dictionary.TryGetValue(key, out TValue value);
                            m_Dictionary.Remove(key);

                            OnDictionaryChanged?.Invoke(new NetworkDictionaryEvent<TKey, TValue>
                            {
                                Type = eventType,
                                Key = key,
                                Value = value
                            });

                            if (keepDirtyDelta)
                            {
                                m_DirtyEvents.Add(new NetworkDictionaryEvent<TKey, TValue>()
                                {
                                    Type = eventType,
                                    Key = key,
                                    Value = value
                                });
                            }
                        }
                        break;
                    case NetworkDictionaryEvent<TKey, TValue>.EventType.Value:
                        {
                            reader.ReadValueSafe(out TKey key);
                            reader.ReadValueSafe(out TValue value);

                            m_Dictionary.TryGetValue(key, out TValue previousValue);
                            m_Dictionary[key] = value;

                            OnDictionaryChanged?.Invoke(new NetworkDictionaryEvent<TKey, TValue>
                            {
                                Type = eventType,
                                Key = key,
                                Value = value,
                                PreviousValue = previousValue
                            });

                            if (keepDirtyDelta)
                            {
                                m_DirtyEvents.Add(new NetworkDictionaryEvent<TKey, TValue>()
                                {
                                    Type = eventType,
                                    Key = key,
                                    Value = value,
                                    PreviousValue = previousValue
                                });
                            }
                        }
                        break;
                    case NetworkDictionaryEvent<TKey, TValue>.EventType.Clear:
                        {
                            m_Dictionary.Clear();

                            OnDictionaryChanged?.Invoke(new NetworkDictionaryEvent<TKey, TValue>
                            {
                                Type = eventType
                            });

                            if (keepDirtyDelta)
                            {
                                m_DirtyEvents.Add(new NetworkDictionaryEvent<TKey, TValue>
                                {
                                    Type = eventType
                                });
                            }
                        }
                        break;
                }
            }
        }

        /// <inheritdoc />
        public IEnumerator<KeyValue<TKey, TValue>> GetEnumerator() => m_Dictionary.GetEnumerator();

        /// <inheritdoc />
        public void Add(TKey key, TValue value)
        {
            m_Dictionary.Add(key, value);

            var dictionaryEvent = new NetworkDictionaryEvent<TKey, TValue>()
            {
                Type = NetworkDictionaryEvent<TKey, TValue>.EventType.Add,
                Key = key,
                Value = value
            };

            HandleAddDictionaryEvent(dictionaryEvent);
        }

        /// <inheritdoc />
        public void Clear()
        {
            m_Dictionary.Clear();

            var dictionaryEvent = new NetworkDictionaryEvent<TKey, TValue>()
            {
                Type = NetworkDictionaryEvent<TKey, TValue>.EventType.Clear
            };

            HandleAddDictionaryEvent(dictionaryEvent);
        }

        /// <inheritdoc />
        public bool ContainsKey(TKey key) => m_Dictionary.ContainsKey(key);

        /// <inheritdoc />
        public bool Remove(TKey key)
        {
            m_Dictionary.TryGetValue(key, out TValue value);
            m_Dictionary.Remove(key);

            var dictionaryEvent = new NetworkDictionaryEvent<TKey, TValue>()
            {
                Type = NetworkDictionaryEvent<TKey, TValue>.EventType.Remove,
                Key = key,
                Value = value
            };

            HandleAddDictionaryEvent(dictionaryEvent);

            return true;
        }

        /// <inheritdoc />
        public bool TryGetValue(TKey key, out TValue value) => m_Dictionary.TryGetValue(key, out value);

        /// <inheritdoc />
        public int Count => m_Dictionary.Count();

        /// <inheritdoc />
        public IEnumerable<TKey> Keys => m_Dictionary.GetKeyArray(Allocator.Temp).ToArray();

        /// <inheritdoc />
        public IEnumerable<TValue> Values => m_Dictionary.GetValueArray(Allocator.Temp).ToArray();

        /// <inheritdoc />
        public TValue this[TKey key]
        {
            get => m_Dictionary[key];
            set
            {
                m_Dictionary[key] = value;

                var dictionaryEvent = new NetworkDictionaryEvent<TKey, TValue>()
                {
                    Type = NetworkDictionaryEvent<TKey, TValue>.EventType.Value,
                    Key = key,
                    Value = value
                };

                HandleAddDictionaryEvent(dictionaryEvent);
            }
        }

        private void HandleAddDictionaryEvent(NetworkDictionaryEvent<TKey, TValue> dictionaryEvent)
        {
            m_DirtyEvents.Add(dictionaryEvent);
            OnDictionaryChanged?.Invoke(dictionaryEvent);
        }

        public override void Dispose()
        {
            m_Dictionary.Dispose();
            m_DirtyEvents.Dispose();
        }
    }

    /// <summary>
    /// Struct containing event information about changes to a NetworkDictionary.
    /// </summary>
    /// <typeparam name="TKey">The type for the dictionary key that the event is about</typeparam>
    /// <typeparam name="TValue">The type for the dictionary value that the event is about</typeparam>
    public struct NetworkDictionaryEvent<TKey, TValue>
    {
        /// <summary>
        /// Enum representing the different operations available for triggering an event.
        /// </summary>
        public enum EventType : byte
        {
            /// <summary>
            /// Add
            /// </summary>
            Add = 0,

            /// <summary>
            /// Remove
            /// </summary>
            Remove = 1,

            /// <summary>
            /// Value changed
            /// </summary>
            Value = 2,

            /// <summary>
            /// Clear
            /// </summary>
            Clear = 3,

            /// <summary>
            /// Full dictionary refresh
            /// </summary>
            Full = 4
        }

        /// <summary>
        /// Enum representing the operation made to the dictionary.
        /// </summary>
        public EventType Type;

        /// <summary>
        /// the key changed, added or removed if available.
        /// </summary>
        public TKey Key;

        /// <summary>
        /// The value changed, added or removed if available.
        /// </summary>
        public TValue Value;

        /// <summary>
        /// The previous value when "Value" has changed, if available.
        /// </summary>
        public TValue PreviousValue;
    }
}