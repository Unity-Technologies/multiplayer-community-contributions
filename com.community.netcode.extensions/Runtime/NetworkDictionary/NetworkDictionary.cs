#if NETWORK_DICTIONARY

using System;
using System.Collections;
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
        public struct Enumerator : IEnumerator<(TKey Key, TValue Value)>
        {
            private NativeArray<TKey> keys;
            private NativeArray<TKey>.Enumerator keysEnumerator;
            private NativeArray<TValue> values;
            private NativeArray<TValue>.Enumerator valuesEnumerator;

            public (TKey Key, TValue Value) Current => (keysEnumerator.Current, valuesEnumerator.Current);

            object IEnumerator.Current => Current;

            public Enumerator(ref NativeList<TKey> keys, ref NativeList<TValue> values)
            {
                this.keys = keys.AsArray();
                this.values = values.AsArray();
                keysEnumerator = new NativeArray<TKey>.Enumerator(ref this.keys);
                valuesEnumerator = new NativeArray<TValue>.Enumerator(ref this.values);
            }

            public void Dispose() { }

            public bool MoveNext()
            {
                var keysEnumeratorCanMove = keysEnumerator.MoveNext();
                var valuesEnumeratorCanMove = valuesEnumerator.MoveNext();

                return keysEnumeratorCanMove && valuesEnumeratorCanMove;
            }

            public void Reset()
            {
                keysEnumerator.Reset();
                valuesEnumerator.Reset();
            }
        }

        private NativeList<TKey> m_Keys = new NativeList<TKey>(64, Allocator.Persistent);
        private NativeList<TValue> m_Values = new NativeList<TValue>(64, Allocator.Persistent);
        private NativeList<TKey> m_KeysAtLastReset = new NativeList<TKey>(64, Allocator.Persistent);
        private NativeList<TValue> m_ValuesAtLastReset = new NativeList<TValue>(64, Allocator.Persistent);
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
                m_Keys.Add(pair.Key);
                m_Values.Add(pair.Value);
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
                m_Keys.Add(pair.Key);
                m_Values.Add(pair.Value);
            }
        }

        /// <inheritdoc />
        public override void ResetDirty()
        {
            base.ResetDirty();

            if (m_DirtyEvents.Length > 0)
            {
                m_DirtyEvents.Clear();
                m_KeysAtLastReset.CopyFrom(m_Keys);
                m_ValuesAtLastReset.CopyFrom(m_Values);
            }
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
                var element = m_DirtyEvents.ElementAt(i);
                writer.WriteValueSafe(m_DirtyEvents[i].Type);

                switch (m_DirtyEvents[i].Type)
                {
                    case NetworkDictionaryEvent<TKey, TValue>.EventType.Add:
                        {
                            NetworkVariableSerialization<TKey>.Write(writer, ref element.Key);
                            NetworkVariableSerialization<TValue>.Write(writer, ref element.Value);
                        }
                        break;
                    case NetworkDictionaryEvent<TKey, TValue>.EventType.Remove:
                        {
                            NetworkVariableSerialization<TKey>.Write(writer, ref element.Key);
                        }
                        break;
                    case NetworkDictionaryEvent<TKey, TValue>.EventType.Value:
                        {
                            NetworkVariableSerialization<TKey>.Write(writer, ref element.Key);
                            NetworkVariableSerialization<TValue>.Write(writer, ref element.Value);
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
            // The keysAtLastReset and valuesAtLastReset mechanism was put in place to deal with duplicate adds
            // upon initial spawn. However, it causes issues with in-scene placed objects
            // due to difference in spawn order. In order to address this, we pick the right
            // list based on the type of object.
            bool isSceneObject = m_NetworkBehaviour.NetworkObject.IsSceneObject != false;

            if (isSceneObject)
            {
                writer.WriteValueSafe((ushort)m_KeysAtLastReset.Length);

                for (int i = 0; i < m_KeysAtLastReset.Length; i++)
                {
                    NetworkVariableSerialization<TKey>.Write(writer, ref m_KeysAtLastReset.ElementAt(i));
                    NetworkVariableSerialization<TValue>.Write(writer, ref m_ValuesAtLastReset.ElementAt(i));
                }
            }
            else
            {
                writer.WriteValueSafe((ushort)m_Keys.Length);

                for (int i = 0; i < m_Keys.Length; i++)
                {
                    NetworkVariableSerialization<TKey>.Write(writer, ref m_Keys.ElementAt(i));
                    NetworkVariableSerialization<TValue>.Write(writer, ref m_Values.ElementAt(i));
                }
            }
        }

        /// <inheritdoc />
        public override void ReadField(FastBufferReader reader)
        {
            m_Keys.Clear();
            m_Values.Clear();

            reader.ReadValueSafe(out ushort count);

            for (int i = 0; i < count; i++)
            {
                NetworkVariableSerialization<TKey>.Read(reader, out TKey key);
                NetworkVariableSerialization<TValue>.Read(reader, out TValue value);
                m_Keys.Add(key);
                m_Values.Add(value);
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
                            NetworkVariableSerialization<TKey>.Read(reader, out TKey key);
                            NetworkVariableSerialization<TValue>.Read(reader, out TValue value);

                            if (m_Keys.Contains(key))
                            {
                                throw new Exception("Shouldn't be here, key already exists in dictionary");
                            }

                            m_Keys.Add(key);
                            m_Values.Add(value);

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
                            NetworkVariableSerialization<TKey>.Read(reader, out TKey key);
                            var index = m_Keys.IndexOf(key);

                            if (index == -1)
                            {
                                break;
                            }

                            var value = m_Values.ElementAt(index);
                            m_Keys.RemoveAt(index);
                            m_Values.RemoveAt(index);

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
                            NetworkVariableSerialization<TKey>.Read(reader, out TKey key);
                            NetworkVariableSerialization<TValue>.Read(reader, out TValue value);
                            var index = m_Keys.IndexOf(key);

                            if (index == -1)
                            {
                                throw new Exception("Shouldn't be here, key doesn't exist in dictionary");
                            }

                            var previousValue = m_Values.ElementAt(index);
                            m_Values[index] = value;

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
                            m_Keys.Clear();
                            m_Values.Clear();

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
        public IEnumerator<(TKey Key, TValue Value)> GetEnumerator() => new Enumerator(ref m_Keys, ref m_Values);

        /// <inheritdoc />
        public void Add(TKey key, TValue value)
        {
            if (m_Keys.Contains(key))
            {
                throw new Exception("Shouldn't be here, key already exists in dictionary");
            }

            m_Keys.Add(key);
            m_Values.Add(value);

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
            m_Keys.Clear();
            m_Values.Clear();

            var dictionaryEvent = new NetworkDictionaryEvent<TKey, TValue>()
            {
                Type = NetworkDictionaryEvent<TKey, TValue>.EventType.Clear
            };

            HandleAddDictionaryEvent(dictionaryEvent);
        }

        /// <inheritdoc />
        public bool ContainsKey(TKey key) => m_Keys.Contains(key);

        /// <inheritdoc />
        public bool Remove(TKey key)
        {
            var index = m_Keys.IndexOf(key);

            if (index == -1)
            {
                return false;
            }

            var value = m_Values[index];
            m_Keys.RemoveAt(index);
            m_Values.RemoveAt(index);

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
        public bool TryGetValue(TKey key, out TValue value)
        {
            var index = m_Keys.IndexOf(key);

            if (index == -1)
            {
                value = default;
                return false;
            }

            value = m_Values[index];
            return true;
        }

        /// <inheritdoc />
        public int Count => m_Keys.Length;

        /// <inheritdoc />
        public IEnumerable<TKey> Keys => m_Keys.ToArray();

        /// <inheritdoc />
        public IEnumerable<TValue> Values => m_Values.ToArray();

        /// <inheritdoc />
        public TValue this[TKey key]
        {
            get
            {
                var index = m_Keys.IndexOf(key);

                if (index == -1)
                {
                    throw new Exception("Shouldn't be here, key doesn't exist in dictionary");
                }

                return m_Values[index];
            }
            set
            {
                var index = m_Keys.IndexOf(key);

                if (index == -1)
                {
                    Add(key, value);
                    return;
                }

                m_Values[index] = value;

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
            MarkNetworkObjectDirty();
            OnDictionaryChanged?.Invoke(dictionaryEvent);
        }

        internal void MarkNetworkObjectDirty()
        {
            m_NetworkBehaviour.NetworkManager.BehaviourUpdater.AddForUpdate(m_NetworkBehaviour.NetworkObject);
        }

        public override void Dispose()
        {
            m_Keys.Dispose();
            m_Values.Dispose();
            m_KeysAtLastReset.Dispose();
            m_ValuesAtLastReset.Dispose();
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

#endif
