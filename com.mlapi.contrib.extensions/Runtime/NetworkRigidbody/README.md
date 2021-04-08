# NetworkRigidbody

Provides a component that replicates a rigidbody's properties over the network. Supports client authority via NetworkObject Ownership. You can also select which properties you want to sync.

Synced properties:

- `Rigidbody.position`
- `Rigidbody.rotation`
- `Rigidbody.velocity`
- `Rigidbody.angularVelocity`

## Interpolation

NetworkRigidbody is linearly interpolated. The interpolation time can be set as a serialized property on the component. Interpolation resets each time network variables are updated.

## Prediction

NetworkRigidbody does not suppress local physics. NetworkObjects not under ownership by the local client will continue to simulate so that movement appears smooth. Rubber banding will be most obvious in scenarios of volatile latency or mixed-ownership collisions.