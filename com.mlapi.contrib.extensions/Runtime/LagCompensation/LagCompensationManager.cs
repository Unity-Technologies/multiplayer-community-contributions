using System;
using System.Collections.Generic;
using MLAPI.Exceptions;
using UnityEngine;

namespace MLAPI.Extensions.LagCompensation
{
    /// <summary>
    /// The main class for controlling lag compensation
    /// </summary>
    public class LagCompensationManager : MonoBehaviour, INetworkUpdateSystem
    {
        public static LagCompensationManager Singleton { get; private set; }
        
        float m_lastNetworkTime = Single.NaN;
        
        [SerializeField]
        float m_SecondsHistory;

        [SerializeField]
        [Tooltip("If true this will sync transform changes after the rollback back to the physics engine so that queries like raycasts use the compensated positions")]
        bool m_SyncTransforms = true;

        /// <summary>
        /// Simulation objects
        /// </summary>
        public readonly List<TrackedObject> SimulationObjects = new List<TrackedObject>();
        
        private void Awake()
        {
            if (Singleton != null && Singleton != this)
            {
                Destroy(gameObject);
                return;
            }

            Singleton = this;

            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            this.RegisterNetworkUpdate(NetworkUpdateStage.EarlyUpdate);
        }

        private void OnDestroy()
        {
            this.UnregisterNetworkUpdate(NetworkUpdateStage.EarlyUpdate);
        }

        /// <summary>
        /// Turns time back a given amount of seconds, invokes an action and turns it back
        /// </summary>
        /// <param name="secondsAgo">The amount of seconds</param>
        /// <param name="action">The action to invoke when time is turned back</param>
        public void Simulate(float secondsAgo, Action action)
        {
            Simulate(secondsAgo, SimulationObjects, action);
        }

        /// <summary>
        /// Turns time back a given amount of second on the given objects, invokes an action and turns it back
        /// </summary>
        /// <param name="secondsAgo">The amount of seconds</param>
        /// <param name="simulatedObjects">The object to simulate back in time</param>
        /// <param name="action">The action to invoke when time is turned back</param>
        public void Simulate(float secondsAgo, IList<TrackedObject> simulatedObjects, Action action)
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                throw new NotServerException("Only the server can perform lag compensation");
            }

            for (int i = 0; i < simulatedObjects.Count; i++)
            {
                simulatedObjects[i].ReverseTransform(secondsAgo);
            }
            
            if (!Physics.autoSyncTransforms && m_SyncTransforms)
            {
                Physics.SyncTransforms();
            }

            action.Invoke();

            for (int i = 0; i < simulatedObjects.Count; i++)
            {
                simulatedObjects[i].ResetStateTransform();
            }
            
            if (!Physics.autoSyncTransforms && m_SyncTransforms)
            {
                Physics.SyncTransforms();
            }
        }

        /// <summary>
        /// Turns time back a given amount of seconds, invokes an action and turns it back. The time is based on the estimated RTT of a clientId
        /// </summary>
        /// <param name="clientId">The clientId's RTT to use</param>
        /// <param name="action">The action to invoke when time is turned back</param>
        public void Simulate(ulong clientId, Action action)
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                throw new NotServerException("Only the server can perform lag compensation");
            }

            float millisecondsDelay = NetworkManager.Singleton.NetworkConfig.NetworkTransport.GetCurrentRtt(clientId) / 2f;
            Simulate(millisecondsDelay * 1000f, action);
        }

        internal void AddFrames()
        {
            for (int i = 0; i < SimulationObjects.Count; i++)
            {
                SimulationObjects[i].AddFrame();
            }
        }

        internal int MaxQueuePoints()
        {
            return (int)(m_SecondsHistory / (1f / NetworkManager.Singleton.NetworkConfig.EventTickrate));
        }

        public void NetworkUpdate(NetworkUpdateStage updateStage)
        {
            switch (updateStage)
            {
                case NetworkUpdateStage.EarlyUpdate:
                    NetworkEarlyUpdate();
                    break;
            }
        }

        private void NetworkEarlyUpdate()
        {
            //This is a check to make sure that we are actually in a new network tick.
            //Initially this was done inside NetworkManager but since LagCompensation is a separate component now we need this safety check.
            //Ideally we would be able to subscribe to a network tick event of the NetworkManager but that does not exist.
            if (m_lastNetworkTime != NetworkManager.Singleton.NetworkTime)
            {
                m_lastNetworkTime = NetworkManager.Singleton.NetworkTime;
                AddFrames();
            }
        }
    }
}
