using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Netcode.Extensions.LagCompensation
{
    /// <summary>
    /// The main class for controlling lag compensation
    /// </summary>
    public class LagCompensationManager : MonoBehaviour
    {
        public static LagCompensationManager Singleton { get; private set; }

        NetworkManager m_NetworkManager;

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

        private void Update()
        {
            if (m_NetworkManager == null)
            {
                var networkManger = NetworkManager.Singleton;
                if (networkManger != null && networkManger.IsServer || networkManger.IsClient) // check if networkmanager is running
                {
                    m_NetworkManager = networkManger;
                    m_NetworkManager.NetworkTickSystem.Tick += AddFrames;
                }
            }
            else
            {
                if (m_NetworkManager.IsServer == false && m_NetworkManager.IsClient == false) // no longer running
                {
                    m_NetworkManager.NetworkTickSystem.Tick -= AddFrames;
                    m_NetworkManager = null;
                }
            }
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
            return (int)(m_SecondsHistory / (1f / NetworkManager.Singleton.NetworkConfig.TickRate));
        }
    }
}
