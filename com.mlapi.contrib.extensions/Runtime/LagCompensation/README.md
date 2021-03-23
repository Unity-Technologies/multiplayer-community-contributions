## How to use

To use lag compensation first add the `LagCompensationManager` component to your NetworkManager gameobject.

Each object which should be tracked for lag compensation needs a `TrackedObject` component on it.

Use `LagCompensationManager.Singleton.Simulate` to rewind objects and perform any action like a raycast.