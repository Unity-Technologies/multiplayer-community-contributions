# Photon Realtime Transport for MLAPI

## Setup

Follow the [transport installation guide](../README.md) to add the Photon Realtime Transport to your project.

To use this transport you must create an Exit Games account. More information in the [License](Runtime/Photon/LICENSE)

### Photon Cloud Setup

Open the Photon Wizard (`Window > Photon Realtime > Wizard`) and setup your app on the Photon Cloud.

### Photon Realtime Transport

- Add the `PhotonRealtimeTransport` component to your GameObject containing your NetworkManager.
- Set the `Network Transport` field on the NetworkManager to the `PhotonRealtimeTransport`
- Enter a room name into the `Room Name` field of the `PhotonRealtimeTransport`.
- Use the MLAPI `StartHost` and `StartClient` functions as usually to host a game and have clients connect to it.

### Rooms/Matchmaking

While a static room name works fine it will put all your players into the same room. You have to write your own logic to set the `RoomName` property of the `PhotonRealtimeTransport` and share that name with all players interested in joining the same room. There is no built in matchmaking currently.

### App Settings

After installing the Photon Realtime Transport There will be a settings file under `Photon/Resources/PhotonAppSettings`. Most settings can be kept at the default value.

`Fixed Region` should be set to a specific region by your game if you want your players to be able to connect to each other by sharing the room name.

Documentation about other settings can be found in the [Photon Documentation](https://doc.photonengine.com/en-us/pun/current/getting-started/initial-setup).