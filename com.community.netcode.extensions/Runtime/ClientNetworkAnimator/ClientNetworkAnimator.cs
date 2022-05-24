using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace Netcode.Extensions
{
    /// <summary>
    /// This imposes state to the server. This is putting trust on your clients. Make sure no security-sensitive features use this animator.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [AddComponentMenu("Netcode/" + nameof(ClientNetworkAnimator))]
    public class ClientNetworkAnimator : NetworkBehaviour
    {
        private void Awake()
        {
            if (GetComponent<NetworkAnimator>())
            {
                Debug.LogWarning(
                    $"ClientNetworkAnimator can not work with the official NetworkAnimator on GameObject {gameObject.name}.");
                enabled = false;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (GetComponent<NetworkAnimator>())
            {
                Debug.LogWarning(
                    $"ClientNetworkAnimator can not work with the official NetworkAnimator on GameObject {gameObject.name}.");
                enabled = false;
            }

            if (IsServer || IsOwner) //we let owner can send messages, too
            {
                m_SendMessagesAllowed = true;
                int layers = m_Animator.layerCount;

                m_TransitionHash = new int[layers];
                m_AnimationHash = new int[layers];
                m_LayerWeights = new float[layers];
            }

            var parameters = m_Animator.parameters;
            m_CachedAnimatorParameters = new NativeArray<AnimatorParamCache>(parameters.Length, Allocator.Persistent);

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];

                if (m_Animator.IsParameterControlledByCurve(parameter.nameHash))
                {
                    // we are ignoring parameters that are controlled by animation curves - syncing the layer
                    //  states indirectly syncs the values that are driven by the animation curves
                    continue;
                }

                var cacheParam = new AnimatorParamCache
                {
                    Type = UnsafeUtility.EnumToInt(parameter.type),
                    Hash = parameter.nameHash
                };

                unsafe
                {
                    switch (parameter.type)
                    {
                        case AnimatorControllerParameterType.Float:
                            var value = m_Animator.GetFloat(cacheParam.Hash);
                            UnsafeUtility.WriteArrayElement(cacheParam.Value, 0, value);
                            break;
                        case AnimatorControllerParameterType.Int:
                            var valueInt = m_Animator.GetInteger(cacheParam.Hash);
                            UnsafeUtility.WriteArrayElement(cacheParam.Value, 0, valueInt);

                            break;
                        case AnimatorControllerParameterType.Bool:
                            var valueBool = m_Animator.GetBool(cacheParam.Hash);
                            UnsafeUtility.WriteArrayElement(cacheParam.Value, 0, valueBool);
                            break;
                        case AnimatorControllerParameterType.Trigger:
                        default:
                            break;
                    }
                }

                m_CachedAnimatorParameters[i] = cacheParam;
            }
        }

        [ServerRpc]
        private unsafe void SubmitAnimStateServerRpc(AnimationMessage animSnapshot,
            ServerRpcParams serverRpcParams = default)
        {
            // edit: we dont need to call the client rpc manually 
            // SendAnimStateClientRpc(animSnapshot);
            if (!IsServer)
            {
                return;
            }

            //Change the animator on server so the server will call client rpc in the next FixedUpdate
            if (animSnapshot.StateHash != 0)
            {
                m_Animator.Play(animSnapshot.StateHash, animSnapshot.Layer, animSnapshot.NormalizedTime);
            }

            m_Animator.SetLayerWeight(animSnapshot.Layer, animSnapshot.Weight);

            if (animSnapshot.Parameters != null && animSnapshot.Parameters.Length != 0)
            {
                // We use a fixed value here to avoid the copy of data from the byte buffer since we own the data
                fixed (byte* parameters = animSnapshot.Parameters)
                {
                    var reader = new FastBufferReader(parameters, Allocator.None, animSnapshot.Parameters.Length);
                    ReadParameters(reader);
                }
            }
        }


        private void FixedUpdate()
        {
            if (!m_SendMessagesAllowed || !m_Animator || !m_Animator.enabled)
            {
                return;
            }

            for (int layer = 0; layer < m_Animator.layerCount; layer++)
            {
                int stateHash;
                float normalizedTime;
                if (!CheckAnimStateChanged(out stateHash, out normalizedTime, layer))
                {
                    continue;
                }

                var animMsg = new AnimationMessage
                {
                    StateHash = stateHash,
                    NormalizedTime = normalizedTime,
                    Layer = layer,
                    Weight = m_LayerWeights[layer]
                };

                m_ParameterWriter.Seek(0);
                m_ParameterWriter.Truncate();

                WriteParameters(m_ParameterWriter);
                animMsg.Parameters = m_ParameterWriter.ToArray();
                if (IsServer)
                {
                    // Debug.Log("sending to client =========================");
                    SendAnimStateClientRpc(animMsg);
                }
                else if (IsClient && IsOwner)
                {
                    // Debug.Log("sending to server =========================");
                    SubmitAnimStateServerRpc(animMsg);
                }
            }
        }

        [ClientRpc]
        private unsafe void SendAnimStateClientRpc(AnimationMessage animSnapshot,
            ClientRpcParams clientRpcParams = default)
        {
            if (IsOwner)
            {
                //We dont send back to ourself
                return;
            }

            if (animSnapshot.StateHash != 0)
            {
                m_Animator.Play(animSnapshot.StateHash, animSnapshot.Layer, animSnapshot.NormalizedTime);
            }

            m_Animator.SetLayerWeight(animSnapshot.Layer, animSnapshot.Weight);

            if (animSnapshot.Parameters != null && animSnapshot.Parameters.Length != 0)
            {
                // We use a fixed value here to avoid the copy of data from the byte buffer since we own the data
                fixed (byte* parameters = animSnapshot.Parameters)
                {
                    var reader = new FastBufferReader(parameters, Allocator.None, animSnapshot.Parameters.Length);
                    ReadParameters(reader);
                }
            }
        }


        /// <summary>
        /// Internally-called RPC client receiving function to update a trigger when the server wants to forward
        ///   a trigger for a client to play / reset
        /// </summary>
        /// <param name="animSnapshot">the payload containing the trigger data to apply</param>
        /// <param name="clientRpcParams">unused</param>
        [ClientRpc]
        private void SendAnimTriggerClientRpc(AnimationTriggerMessage animSnapshot,
            ClientRpcParams clientRpcParams = default)
        {
            if (IsOwner)
            {
                return; //again, we dont want to send the rpc back to the owner
            }

            if (animSnapshot.Reset)
            {
                m_Animator.ResetTrigger(animSnapshot.Hash);
            }
            else
            {
                m_Animator.SetTrigger(animSnapshot.Hash);
            }
        }

        /// <inheritdoc cref="SetTrigger(string)" />
        /// <param name="hash">The hash for the trigger to activate</param>
        /// <param name="reset">If true, resets the trigger</param>
        public void SetTrigger(int hash, bool reset = false)
        {
            var animMsg = new AnimationTriggerMessage();
            animMsg.Hash = hash;
            animMsg.Reset = reset;

            if (reset)
            {
                m_Animator.ResetTrigger(hash);
            }
            else
            {
                m_Animator.SetTrigger(hash);
            }

            if (IsServer)
            {
                SendAnimTriggerClientRpc(animMsg);
            }
            else if (IsOwner)
            {
                // Debug.LogWarning("Trying to call NetworkAnimator.SetTrigger on a client...ignoring");
                // Sorry, official LogWaring, we want the call set trigger on a client
                SubmitAnimTriggerServerRpc(animMsg);
            }
        }

        [ServerRpc]
        private void SubmitAnimTriggerServerRpc(AnimationTriggerMessage animSnapshot,
            ServerRpcParams serverRpcParams = default)
        {
            if (!IsServer)
            {
                return;
            }

            if (animSnapshot.Reset)
            {
                m_Animator.ResetTrigger(animSnapshot.Hash);
            }
            else
            {
                m_Animator.SetTrigger(animSnapshot.Hash);
            }

            //now we have to call all clients manually because the trigger will not update itself in the FixedUpdate
            SendAnimTriggerClientRpc(animSnapshot);
        }


        #region Origion NetworkAnimator

        internal struct AnimationMessage : INetworkSerializable
        {
            // state hash per layer.  if non-zero, then Play() this animation, skipping transitions
            public int StateHash;
            public float NormalizedTime;
            public int Layer;
            public float Weight;
            public byte[] Parameters;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref StateHash);
                serializer.SerializeValue(ref NormalizedTime);
                serializer.SerializeValue(ref Layer);
                serializer.SerializeValue(ref Weight);
                serializer.SerializeValue(ref Parameters);
            }
        }

        internal struct AnimationTriggerMessage : INetworkSerializable
        {
            public int Hash;
            public bool Reset;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Hash);
                serializer.SerializeValue(ref Reset);
            }
        }

        [SerializeField] private Animator m_Animator;

        public Animator Animator
        {
            get { return m_Animator; }
            set { m_Animator = value; }
        }

        private bool m_SendMessagesAllowed = false;

        // Animators only support up to 32 params
        public static int K_MaxAnimationParams = 32;

        private int[] m_TransitionHash;
        private int[] m_AnimationHash;
        private float[] m_LayerWeights;

        private unsafe struct AnimatorParamCache
        {
            public int Hash;
            public int Type;
            public fixed byte Value[4]; // this is a max size of 4 bytes
        }

        // 128 bytes per Animator
        private FastBufferWriter m_ParameterWriter =
            new FastBufferWriter(K_MaxAnimationParams * sizeof(float), Allocator.Persistent);

        private NativeArray<AnimatorParamCache> m_CachedAnimatorParameters;

        // We cache these values because UnsafeUtility.EnumToInt uses direct IL that allows a non-boxing conversion
        private struct AnimationParamEnumWrapper
        {
            public static readonly int AnimatorControllerParameterInt;
            public static readonly int AnimatorControllerParameterFloat;
            public static readonly int AnimatorControllerParameterBool;

            static AnimationParamEnumWrapper()
            {
                AnimatorControllerParameterInt = UnsafeUtility.EnumToInt(AnimatorControllerParameterType.Int);
                AnimatorControllerParameterFloat = UnsafeUtility.EnumToInt(AnimatorControllerParameterType.Float);
                AnimatorControllerParameterBool = UnsafeUtility.EnumToInt(AnimatorControllerParameterType.Bool);
            }
        }

        public override void OnDestroy()
        {
            if (m_CachedAnimatorParameters.IsCreated)
            {
                m_CachedAnimatorParameters.Dispose();
            }

            m_ParameterWriter.Dispose();
        }


        public override void OnNetworkDespawn()
        {
            m_SendMessagesAllowed = false;
        }

        private bool CheckAnimStateChanged(out int stateHash, out float normalizedTime, int layer)
        {
            bool shouldUpdate = false;
            stateHash = 0;
            normalizedTime = 0;

            float layerWeightNow = m_Animator.GetLayerWeight(layer);

            if (!Mathf.Approximately(layerWeightNow, m_LayerWeights[layer]))
            {
                m_LayerWeights[layer] = layerWeightNow;
                shouldUpdate = true;
            }

            if (m_Animator.IsInTransition(layer))
            {
                AnimatorTransitionInfo tt = m_Animator.GetAnimatorTransitionInfo(layer);
                if (tt.fullPathHash != m_TransitionHash[layer])
                {
                    // first time in this transition for this layer
                    m_TransitionHash[layer] = tt.fullPathHash;
                    m_AnimationHash[layer] = 0;
                    shouldUpdate = true;
                }
            }
            else
            {
                //TODO: there is no way to tell if the BlendTree has changed. Maybe try something below:
                // var clips = m_Animator.GetCurrentAnimatorClipInfo(layer);
                // this will return more than 1 element if the current state is a blend tree
                // but only works when the blend tree is blending more than 1 animation clip
                AnimatorStateInfo st = m_Animator.GetCurrentAnimatorStateInfo(layer);
                if (st.fullPathHash != m_AnimationHash[layer])
                {
                    // first time in this animation state
                    if (m_AnimationHash[layer] != 0)
                    {
                        // came from another animation directly - from Play()
                        stateHash = st.fullPathHash;
                        normalizedTime = st.normalizedTime;
                    }

                    m_TransitionHash[layer] = 0;
                    m_AnimationHash[layer] = st.fullPathHash;
                    shouldUpdate = true;
                }
            }

            return shouldUpdate;
        }

        /* $AS TODO: Right now we are not checking for changed values this is because
        the read side of this function doesn't have similar logic which would cause
        an overflow read because it doesn't know if the value is there or not. So
        there needs to be logic to track which indexes changed in order for there
        to be proper value change checking. Will revist in 1.1.0.
        */
        private unsafe void WriteParameters(FastBufferWriter writer)
        {
            for (int i = 0; i < m_CachedAnimatorParameters.Length; i++)
            {
                ref var cacheValue =
                    ref UnsafeUtility.ArrayElementAsRef<AnimatorParamCache>(m_CachedAnimatorParameters.GetUnsafePtr(),
                        i);
                var hash = cacheValue.Hash;

                if (cacheValue.Type == AnimationParamEnumWrapper.AnimatorControllerParameterInt)
                {
                    var valueInt = m_Animator.GetInteger(hash);
                    fixed (void* value = cacheValue.Value)
                    {
                        UnsafeUtility.WriteArrayElement(value, 0, valueInt);
                        BytePacker.WriteValuePacked(writer, (uint)valueInt);
                    }
                }
                else if (cacheValue.Type == AnimationParamEnumWrapper.AnimatorControllerParameterBool)
                {
                    var valueBool = m_Animator.GetBool(hash);
                    fixed (void* value = cacheValue.Value)
                    {
                        UnsafeUtility.WriteArrayElement(value, 0, valueBool);
                        writer.WriteValueSafe(valueBool);
                    }
                }
                else if (cacheValue.Type == AnimationParamEnumWrapper.AnimatorControllerParameterFloat)
                {
                    var valueFloat = m_Animator.GetFloat(hash);
                    fixed (void* value = cacheValue.Value)
                    {
                        UnsafeUtility.WriteArrayElement(value, 0, valueFloat);
                        writer.WriteValueSafe(valueFloat);
                    }
                }
            }
        }

        private unsafe void ReadParameters(FastBufferReader reader)
        {
            for (int i = 0; i < m_CachedAnimatorParameters.Length; i++)
            {
                ref var cacheValue =
                    ref UnsafeUtility.ArrayElementAsRef<AnimatorParamCache>(m_CachedAnimatorParameters.GetUnsafePtr(),
                        i);
                var hash = cacheValue.Hash;

                if (cacheValue.Type == AnimationParamEnumWrapper.AnimatorControllerParameterInt)
                {
                    ByteUnpacker.ReadValuePacked(reader, out int newValue);
                    m_Animator.SetInteger(hash, newValue);
                    fixed (void* value = cacheValue.Value)
                    {
                        UnsafeUtility.WriteArrayElement(value, 0, newValue);
                    }
                }
                else if (cacheValue.Type == AnimationParamEnumWrapper.AnimatorControllerParameterBool)
                {
                    reader.ReadValueSafe(out bool newBoolValue);
                    m_Animator.SetBool(hash, newBoolValue);
                    fixed (void* value = cacheValue.Value)
                    {
                        UnsafeUtility.WriteArrayElement(value, 0, newBoolValue);
                    }
                }
                else if (cacheValue.Type == AnimationParamEnumWrapper.AnimatorControllerParameterFloat)
                {
                    reader.ReadValueSafe(out float newFloatValue);
                    m_Animator.SetFloat(hash, newFloatValue);
                    fixed (void* value = cacheValue.Value)
                    {
                        UnsafeUtility.WriteArrayElement(value, 0, newFloatValue);
                    }
                }
            }
        }

        // [ClientRpc]
        // private void SendAnimTriggerClientRpc(AnimationTriggerMessage animSnapshot,
        //     ClientRpcParams clientRpcParams = default)
        // {
        //     if (animSnapshot.Reset)
        //     {
        //         m_Animator.ResetTrigger(animSnapshot.Hash);
        //     }
        //     else
        //     {
        //         m_Animator.SetTrigger(animSnapshot.Hash);
        //     }
        // }

        /// <summary>
        /// Sets the trigger for the associated animation
        ///  Note, triggers are special vs other kinds of parameters.  For all the other parameters we watch for changes
        ///  in FixedUpdate and users can just set them normally off of Animator. But because triggers are transitory
        ///  and likely to come and go between FixedUpdate calls, we require users to set them here to guarantee us to
        ///  catch it...then we forward it to the Animator component
        /// </summary>
        /// <param name="triggerName">The string name of the trigger to activate</param>
        public void SetTrigger(string triggerName)
        {
            SetTrigger(Animator.StringToHash(triggerName));
        }

        /// <inheritdoc cref="SetTrigger(string)" />
        /// <param name="hash">The hash for the trigger to activate</param>
        /// <param name="reset">If true, resets the trigger</param>
        // public void SetTrigger(int hash, bool reset = false)
        // {
        //     var animMsg = new AnimationTriggerMessage();
        //     animMsg.Hash = hash;
        //     animMsg.Reset = reset;
        //
        //     if (IsServer)
        //     {
        //         //  trigger the animation locally on the server...
        //         if (reset)
        //         {
        //             m_Animator.ResetTrigger(hash);
        //         }
        //         else
        //         {
        //             m_Animator.SetTrigger(hash);
        //         }
        //
        //         // ...then tell all the clients to do the same
        //         SendAnimTriggerClientRpc(animMsg);
        //     }
        //     else
        //     {
        //         Debug.LogWarning("Trying to call NetworkAnimator.SetTrigger on a client...ignoring");
        //     }
        // }

        /// <summary>
        /// Resets the trigger for the associated animation.  See <see cref="SetTrigger(string)">SetTrigger</see> for more on how triggers are special
        /// </summary>
        /// <param name="triggerName">The string name of the trigger to reset</param>
        public void ResetTrigger(string triggerName)
        {
            ResetTrigger(Animator.StringToHash(triggerName));
        }

        /// <inheritdoc cref="ResetTrigger(string)" path="summary" />
        /// <param name="hash">The hash for the trigger to activate</param>
        public void ResetTrigger(int hash)
        {
            SetTrigger(hash, true);
        }

        #endregion
    }
}