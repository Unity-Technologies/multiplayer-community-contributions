# SteamNetworking Transport for Unity NetCode for GameObjects
The SteamNetworking Transport leverages Valve's SteamNetworking APIs enabling secure and efficent networking in both peer to peer and client server architectures. The Steam networking APIs address via CSteamID, not IP/Port. These APIs handle routing via Valve's backend services and do not require NAT punch or additional routing solutions.

## Dependencies
**[Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET)** This transport relies on Steamworks.NET to communicate with the **[Steamworks API](https://partner.steamgames.com/doc/sdk)**. 
**[Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) its self requires .Net 4.x**  

## Set Up

1. Install [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) via the package manager by clicking the '+' (plus) button located in the upper left of the window and selecting `Add package from git URL...` when prompted provide the following URL:  
`https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net`
2. Install this package via the package manager by clicking the '+' (plus) button located in the upper left of the window and selecting `Add package from git URL...` when prompted provide the following URL:  
`https://github.com/Unity-Technologies/mlapi-community-contributions.git?path=/Transports/com.mlapi.contrib.transport.steamnetworking`

## Usage
This transport does require that you first initalize the Steam API before use. To do so you will need to either

- Author your own initalization logic using the documentation provided by [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET)
- Use a 3rd party solution such as [Steamworks V2 Foundaiton (free)](https://assetstore.unity.com/packages/tools/integration/steamworks-v2-foundation-186949) or [Steamworks V2 Complete (paid)](https://assetstore.unity.com/packages/tools/integration/steamworks-v2-complete-190316) or comparable solution
- Use the example SteamManager from [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) **NOTE This is not recomended as the SteamManager does not support Steam Game Server and is very limited in funcitonlity, it can however be a good learning tool for creating your own logic**


Steam Networking uses the CSteamID as the network address to connect to. For P2P games this would require you to provide the Steam ID of the peer to connect to. For Client Server games this would require you to log your server onto Steam as a Steam Game Server, this act will issue your server a Steam ID which would be used as the address in this transport.
