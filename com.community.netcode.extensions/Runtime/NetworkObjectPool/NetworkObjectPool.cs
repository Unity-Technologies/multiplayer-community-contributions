using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

namespace Netcode.Extensions
{
    public class NetworkObjectPool : MonoBehaviour
    {
        [SerializeField]
        NetworkManager m_NetworkManager;

        [SerializeField]
        List<PoolConfigObject> PooledPrefabsList;

        HashSet<GameObject> prefabs = new HashSet<GameObject>();

        Dictionary<GameObject, Queue<NetworkObject>> pooledObjects = new Dictionary<GameObject, Queue<NetworkObject>>();

        public void Awake()
        {
            InitializePool();
        }

        public void OnValidate()
        {
            for (var i = 0; i < PooledPrefabsList.Count; i++)
            {
                var prefab = PooledPrefabsList[i].Prefab;
                if (prefab != null)
                {
                    Assert.IsNotNull(prefab.GetComponent<NetworkObject>(), $"{nameof(NetworkObjectPool)}: Pooled prefab \"{prefab.name}\" at index {i.ToString()} has no {nameof(NetworkObject)} component.");
                }
            }
        }

        /// <summary>
        /// Gets an instance of the given prefab from the pool. The prefab must be registered to the pool.
        /// </summary>
        /// <param name="prefab"></param>
        /// <returns></returns>
        public NetworkObject GetNetworkObject(GameObject prefab)
        {
            return GetNetworkObjectInternal(prefab, Vector3.zero, Quaternion.identity);
        }

        /// <summary>
        /// Gets an instance of the given prefab from the pool. The prefab must be registered to the pool.
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="position">The position to spawn the object at.</param>
        /// <param name="rotation">The rotation to spawn the object with.</param>
        /// <returns></returns>
        public NetworkObject GetNetworkObject(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            return GetNetworkObjectInternal(prefab, position, rotation);
        }

        /// <summary>
        /// Return an object to the pool (and reset them).
        /// </summary>
        public void ReturnNetworkObject(NetworkObject networkObject, GameObject prefab)
        {
            var go = networkObject.gameObject;

            // In this simple example pool we just disable objects while they are in the pool. But we could call a function on the object here for more flexibility.
            go.SetActive(false);
            go.transform.SetParent(transform);
            pooledObjects[prefab].Enqueue(networkObject);
        }

        /// <summary>
        /// Adds a prefab to the list of spawnable prefabs.
        /// </summary>
        /// <param name="prefab">The prefab to add.</param>
        /// <param name="prewarmCount"></param>
        public void AddPrefab(GameObject prefab, int prewarmCount = 0)
        {
            var networkObject = prefab.GetComponent<NetworkObject>();

            Assert.IsNotNull(networkObject, $"{nameof(prefab)} must have {nameof(networkObject)} component.");
            Assert.IsFalse(prefabs.Contains(prefab), $"Prefab {prefab.name} is already registered in the pool.");

            RegisterPrefabInternal(prefab, prewarmCount);
        }

        /// <summary>
        /// Builds up the cache for a prefab.
        /// </summary>
        private void RegisterPrefabInternal(GameObject prefab, int prewarmCount)
        {
            prefabs.Add(prefab);

            var prefabQueue = new Queue<NetworkObject>();
            pooledObjects[prefab] = prefabQueue;

            for (int i = 0; i < prewarmCount; i++)
            {
                var go = CreateInstance(prefab);
                ReturnNetworkObject(go.GetComponent<NetworkObject>(), prefab);
            }

            // Register netcode Spawn handlers
            m_NetworkManager.PrefabHandler.AddHandler(prefab, new DummyPrefabInstanceHandler(prefab, this));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private GameObject CreateInstance(GameObject prefab)
        {
            return Instantiate(prefab);
        }

        /// <summary>
        /// This matches the signature of <see cref="NetworkSpawnManager.SpawnHandlerDelegate"/>
        /// </summary>
        /// <param name="prefabHash"></param>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        /// <returns></returns>
        private NetworkObject GetNetworkObjectInternal(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            var queue = pooledObjects[prefab];

            NetworkObject networkObject;
            if (queue.Count > 0)
            {
                networkObject = queue.Dequeue();
            }
            else
            {
                networkObject = CreateInstance(prefab).GetComponent<NetworkObject>();
            }

            // Here we must reverse the logic in ReturnNetworkObject.
            var go = networkObject.gameObject;
            go.transform.SetParent(null);
            go.SetActive(true);

            go.transform.position = position;
            go.transform.rotation = rotation;

            return networkObject;
        }

        /// <summary>
        /// Registers all objects in <see cref="PooledPrefabsList"/> to the cache.
        /// </summary>
        private void InitializePool()
        {
            foreach (var configObject in PooledPrefabsList)
            {
                RegisterPrefabInternal(configObject.Prefab, configObject.PrewarmCount);
            }
        }
    }

    [Serializable]
    struct PoolConfigObject
    {
        public GameObject Prefab;
        public int PrewarmCount;
    }

    class DummyPrefabInstanceHandler : INetworkPrefabInstanceHandler
    {
        GameObject m_Prefab;
        NetworkObjectPool m_Pool;

        public DummyPrefabInstanceHandler(GameObject prefab, NetworkObjectPool pool)
        {
            m_Prefab = prefab;
            m_Pool = pool;
        }

        public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            return m_Pool.GetNetworkObject(m_Prefab, position, rotation);
        }

        public void Destroy(NetworkObject networkObject)
        {
            m_Pool.ReturnNetworkObject(networkObject, m_Prefab);
        }
    }
}
