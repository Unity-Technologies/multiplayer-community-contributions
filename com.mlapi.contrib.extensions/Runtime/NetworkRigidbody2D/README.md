# NetworkRigidbody2D

Provides a component that syncs Rigidbody2Ds over the network.

### Synced properties
- `Rigidbody.velocity`
- `Rigidbody.angularVelocity`

### Prediction
NetworkRigidbody2D does not suppress local physics. NetworkObjects not not under ownership by the server will continue to simulate so that the movement appears smooth. 