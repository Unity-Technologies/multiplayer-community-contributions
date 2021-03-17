## NetworkObjectPool

NetworkObjectPool is a generic object pool script which works as an example out of the box solution for how to pool objects with MLAPI.

To use this just add it to your scene with the NetworkingManager and register all objects you want to get auto pooled.

Client objects will get automatically pooled. To pool server objects instead of destroying them call `NetworkObject.Despawn();` and then `NetworkObjectPool.ReturnNetworkObject(NetworkObject);`.