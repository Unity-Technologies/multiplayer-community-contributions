The MLAPI Community Contributions repository contains extensions to [MLAPI](https://github.com/Unity-Technologies/com.unity.multiplayer.mlapi) provided by the community.

### How to use

[Installing a Community Transport](/Transports/README.md)

[Installing the Community Extensions Package](/com.mlapi.contrib.extensions/README.md)

### Community and Feedback
For general questions, networking advice or discussions about MLAPI and its extensions, please join our [Discord Community](https://discord.gg/FM8SE9E) or create a post in the [Unity Multiplayer Forum](https://forum.unity.com/forums/multiplayer.26/).

### Maintenance
The contributions repository is a community repository and not an official Unity product. What this means is:
- We will accept new content and bug fixes and try to keep the content in this repository up to date.
- We do not guarantee that any of the content in this repository will be supported by future MLAPI versions.
- We ask the community and authors to maintain the content in this repository. Unity is not responsible for fixing bugs in community content.

### Adding new content
Check our [contribution guidelines](CONTRIBUTING.md) for information on how to contribute to this repository.

### Existing Content

#### Transports
| **Name** | **Platforms** | **Version Specifics** | **Latest MLAPI** | **v12** |
|:------------:|:---------:|:-------------:|:-------:|:---:|
| **[Ruffles](/Transports/com.mlapi.contrib.transport.ruffles)**| Desktop, Mobile | | :heavy_check_mark: | :heavy_check_mark: | 
|**[Enet](/Transports/com.mlapi.contrib.transport.enet)**| Desktop, Mobile\* | |:heavy_check_mark: | :heavy_check_mark: | 
|**[LiteNetLib](/Transports/com.mlapi.contrib.transport.litenetlib)**| Desktop, Mobile | | :heavy_check_mark: | :heavy_check_mark: | 
|**[SteamP2P](/Transports/com.mlapi.contrib.transport.steamp2p)**| Steam || :heavy_check_mark: | :heavy_check_mark: | 
|**WebSocket**| Desktop, Mobile, WebGL | | :x:| :x: |
|**[Photon Realtime](/Transports/com.mlapi.contrib.transport.photon-realtime)**| Desktop, Mobile, WebGL\** || :heavy_check_mark: | |  

\* Needs manual binary compilation.<br>
\** Other platforms such as console platforms are also supported but require communication with Exit Games.

#### Extensions
| **Name** | **Version Specifics** | **Latest MLAPI** | **v12** |
|:------------:|:-------------:|:-------:|:---:|
|**[NetworkObjectPool](/com.mlapi.contrib.extensions/Runtime/NetworkObjectPool)**| | :heavy_check_mark: | |
|**[NetworkManagerHud](/com.mlapi.contrib.extensions/Runtime/NetworkManagerHud)**| | :heavy_check_mark: | |
|**[NetworkRigidBody](/com.mlapi.contrib.extensions/Runtime/NetworkRigidbody)**| | :heavy_check_mark: | |


### Releases
Content for a specifc major version of MLAPI can be found in the release branches. The following release branches
exist:
| **Release**|
|:------------:|
| **[v12](https://github.com/Unity-Technologies/MLAPI.Transports/tree/release-v12)**|
