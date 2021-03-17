using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MLAPI.Spawning;
using UnityEngine;
using UnityEngine.Assertions;

namespace MLAPI.Extensions
{
    public class NetworkObjectPool : MonoBehaviour
    {
        [SerializeField]
        List<PoolConfigObject> PooledPrefabsList;

        Dictionary<ulong, GameObject> prefabs = new Dictionary<ulong, GameObject>();

        Dictionary<ulong, Queue<NetworkObject>> pooledObjects = new Dictionary<ulong, Queue<NetworkObject>>();

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
        /// Gets a instance of a network object based on the prefab hash.
        /// </summary>
        /// <param name="prefabHash">The prefab hash to identify the object.</param>
        /// <returns></returns>
        public GameObject GetNetworkObject(ulong prefabHash)
        {
            return GetNetworkObjectInternal(prefabHash, Vector3.zero, Quaternion.identity).gameObject;
        }

        /// <summary>
        /// Gets a instance of a network object based on the prefab hash.
        /// </summary>
        /// <param name="prefabHash">The prefab hash to identify the object.</param>
        /// <param name="position">The position to spawn the object at.</param>
        /// <param name="rotation">The rotation to spawn the object with.</param>
        /// <returns></returns>
        public GameObject GetNetworkObject(ulong prefabHash, Vector3 position, Quaternion rotation)
        {
            return GetNetworkObjectInternal(prefabHash, position, rotation).gameObject;
        }

        /// <summary>
        /// Gets an instance of the given prefab from the pool. The prefab must be registered to the pool.
        /// </summary>
        /// <param name="prefab"></param>
        /// <returns></returns>
        public GameObject GetNetworkObject(GameObject prefab)
        {
            var networkObject = prefab.GetComponent<NetworkObject>();

            Assert.IsNotNull(networkObject, $"{nameof(prefab)} must have {nameof(networkObject)} component.");

            return GetNetworkObject(networkObject.PrefabHash);
        }
        
        /// <summary>
        /// Return an object to the pool (and reset them).
        /// </summary>
        public void ReturnNetworkObject(NetworkObject networkObject)
        {
            var go = networkObject.gameObject;

            // In this simple example pool we just disable objects while they are in the pool. But we could call a function on the object here for more flexibility.
            go.SetActive(false);
            go.transform.SetParent(transform);
            pooledObjects[networkObject.PrefabHash].Enqueue(networkObject);
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
            Assert.IsFalse(prefabs.ContainsKey(networkObject.PrefabHash), $"Prefab {prefab.name} (PrefabHashGenerator: {networkObject.PrefabHashGenerator}) is already registered in the pool.");

            RegisterPrefabInternal(prefab, prewarmCount);
        }

        /// <summary>
        /// Builds up the cache for a prefab.
        /// </summary>
        private void RegisterPrefabInternal(GameObject prefab, int prewarmCount)
        {
            var networkObject = prefab.GetComponent<NetworkObject>();
            var prefabHash = networkObject.PrefabHash;

            prefabs[prefabHash] = prefab;

            var prefabQueue = new Queue<NetworkObject>();
            pooledObjects[prefabHash] = prefabQueue;

            for (int i = 0; i < prewarmCount; i++)
            {
                var go = CreateInstance(prefabHash);
                ReturnNetworkObject(go.GetComponent<NetworkObject>());
            }

            // Register MLAPI Spawn handlers

            NetworkSpawnManager.RegisterDestroyHandler(prefabHash, ReturnNetworkObject);
            NetworkSpawnManager.RegisterSpawnHandler(prefabHash, (position, rotation) => GetNetworkObjectInternal(prefabHash, position, rotation));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private GameObject CreateInstance(ulong prefabHash)
        {
            return Instantiate(prefabs[prefabHash]);
        }

        /// <summary>
        /// This matches the signature of <see cref="NetworkSpawnManager.SpawnHandlerDelegate"/>
        /// </summary>
        /// <param name="prefabHash"></param>
        /// <param name="position"></param>
        /// <param name="rotation"></param>
        /// <returns></returns>
        private NetworkObject GetNetworkObjectInternal(ulong prefabHash, Vector3 position, Quaternion rotation)
        {
            var queue = pooledObjects[prefabHash];

            NetworkObject networkObject;
            if (queue.Count > 0)
            {
                networkObject = queue.Dequeue();
            }
            else
            {
                networkObject = CreateInstance(prefabHash).GetComponent<NetworkObject>();
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
}
