using System.Collections.Generic;
using MLAPI.Collections;
using UnityEngine;
using UnityEngine.Assertions;

namespace MLAPI.Extensions.LagCompensation
{
    //Based on: https://twotenpvp.github.io/lag-compensation-in-unity.html
    //Modified to be used with latency rather than fixed frames and subframes. Thus it will be less accrurate but more modular.

    /// <summary>
    /// A component used for lag compensation. Each object with this component will get tracked
    /// </summary>
    [AddComponentMenu("MLAPI/TrackedObject", -98)]
    public class TrackedObject : MonoBehaviour
    {
        Dictionary<float, TrackedPoint> m_FrameData = new Dictionary<float, TrackedPoint>();
        FixedQueue<float> m_Framekeys;
        int m_MaxPoints;

        Vector3 savedPosition;
        Quaternion savedRotation;


        LagCompensationManager m_LagCompensationManager;

        private void Start()
        {
            Assert.IsNotNull(LagCompensationManager.Singleton, $"{nameof(TrackedObject)} needs a {nameof(LagCompensationManager)}. Add a {nameof(LagCompensationManager)} to your scene.");
            m_LagCompensationManager = LagCompensationManager.Singleton;           
            
            m_MaxPoints = m_LagCompensationManager.MaxQueuePoints();
            
            m_Framekeys = new FixedQueue<float>(m_MaxPoints);
            m_Framekeys.Enqueue(0);
            m_LagCompensationManager.SimulationObjects.Add(this);
        }
        
        /// <summary>
        /// Gets the total amount of points stored in the component
        /// </summary>
        public int TotalPoints
        {
            get
            {
                if (m_Framekeys == null) return 0;
                else return m_Framekeys.Count;
            }
        }

        /// <summary>
        /// Gets the average amount of time between the points in miliseconds
        /// </summary>
        public float AvgTimeBetweenPointsMs
        {
            get
            {
                if (m_Framekeys == null || m_Framekeys.Count == 0) return 0;
                else return ((m_Framekeys.ElementAt(m_Framekeys.Count - 1) - m_Framekeys.ElementAt(0)) / m_Framekeys.Count) * 1000f;
            }
        }

        /// <summary>
        /// Gets the total time history we have for this object
        /// </summary>
        public float TotalTimeHistory
        {
            get
            {
                if (m_Framekeys == null) return 0;
                else return m_Framekeys.ElementAt(m_Framekeys.Count - 1) - m_Framekeys.ElementAt(0);
            }
        }

        internal void ReverseTransform(float secondsAgo)
        {
            savedPosition = transform.position;
            savedRotation = transform.rotation;

            float currentTime = NetworkManager.Singleton.NetworkTime;
            float targetTime = currentTime - secondsAgo;

            float previousTime = 0f;
            float nextTime = 0f;
            for (int i = 0; i < m_Framekeys.Count; i++)
            {
                if (previousTime <= targetTime && m_Framekeys.ElementAt(i) >= targetTime)
                {
                    nextTime = m_Framekeys.ElementAt(i);
                    break;
                }
                else
                    previousTime = m_Framekeys.ElementAt(i);
            }
            float timeBetweenFrames = nextTime - previousTime;
            float timeAwayFromPrevious = currentTime - previousTime;
            float lerpProgress = timeAwayFromPrevious / timeBetweenFrames;
            transform.position = Vector3.Lerp(m_FrameData[previousTime].position, m_FrameData[nextTime].position, lerpProgress);
            transform.rotation = Quaternion.Slerp(m_FrameData[previousTime].rotation, m_FrameData[nextTime].rotation, lerpProgress);
        }

        internal void ResetStateTransform()
        {
            transform.position = savedPosition;
            transform.rotation = savedRotation;
        }


        void OnDestroy()
        {
            m_LagCompensationManager.SimulationObjects.Remove(this);
        }

        internal void AddFrame()
        {
            if (m_Framekeys.Count == m_MaxPoints)
                m_FrameData.Remove(m_Framekeys.Dequeue());

            m_FrameData.Add(NetworkManager.Singleton.NetworkTime, new TrackedPoint()
            {
                position = transform.position,
                rotation = transform.rotation
            });
            m_Framekeys.Enqueue(NetworkManager.Singleton.NetworkTime);
        }
    }
}
