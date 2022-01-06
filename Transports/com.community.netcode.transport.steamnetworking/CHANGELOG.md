# Changelog
All notable changes to this package will be documented in this file. The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)

v2.0.0 submitted by [Heathen Engineering](https://assetstore.unity.com/publishers/5836). The objective of these changes is to allow this transport to 

1. Work with any implamentation of [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) including [Heathen's Steamworks V2](https://assetstore.unity.com/packages/tools/integration/steamworks-v2-complete-190316)
2. To enable this transport to be used in both Peer to Peer and Client Server architectures

The [Heathen Assets Discord](https://discord.gg/6X3xrRc) server can be used to ask any questions regarding Heathen's modifications or to see community support with Heathen related Steam integration and Steam networking questions

## [2.0.1] - 2021-01-06

### Fixed
- Fixed a bug in the internal channel implementation which did not allow Netcode for GameObject to send any data over the transport.

## [2.0.0] - 2021-11-25
### Add
- Added support for Steam Game Server Networking APIs

### Changed
- Transport is now named SteamNetworkingTransport as it is no longer limited to peer to peer architectures
- Namespace simplified to Netcode.Transports
- Updated all API calls to test for platform, in the case of UNITY_SERVER being defined the transport will use the SteamGameServerNetworking APIs otherwise it will use the client equivelent SteamNetworking APIs

### Removed
- dependency on SteamManager and SteamManager code has been removed. This makes it easier for users to use whatever initalization logic the user wishes including SteamManager but also custom logic and 3rd party logic such as SteamworksBehaviour from [Heathen's Steamworks V2](https://assetstore.unity.com/packages/tools/integration/steamworks-v2-complete-190316)
- pre-packaged Steamworks.NET; this being removed allows the user to use whatever version they please including other 3rd party extensions such as [Heathen's Steamworks V2](https://assetstore.unity.com/packages/tools/integration/steamworks-v2-complete-190316)
- Removed unused and unessisary Client API calls such as calls to SteamUser in various debug log messages

## 1.0.0
First version of the Steam transport as a Unity package.