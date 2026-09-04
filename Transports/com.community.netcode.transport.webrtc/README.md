# WebRTC Transport for Netcode for GameObjects

A cross-platform [Netcode for GameObjects](https://docs-multiplayer.unity3d.com/) transport that uses WebRTC data channels. It supports Desktop, Mobile, and WebGL, including crossplay between those platforms.

Tested on Windows, Mobile, and WebGL with Netcode for GameObjects 2.4.2.

## Architecture

| Layer | Platform | Components |
| --- | --- | --- |
| C# | All | `WebRTCTransport`, platform-specific peer connections and signaling clients |
| Native WebRTC | Editor, Desktop, Mobile | [Unity WebRTC](https://docs.unity3d.com/Packages/com.unity.webrtc@3.0/manual/index.html) |
| WebGL | WebGL player | Packaged `.jslib` + `webrtc-web-client.jspre` (no custom WebGL template required) |
| Signaling | All | Socket.IO. Sample server: [webrtc-ngo-signaling](https://github.com/aziztitu/webrtc-ngo-signaling) |

## Dependencies

Install these in your Unity project before using the transport:

1. **Netcode for GameObjects** 2.0.0 or newer
2. **Unity WebRTC** (`com.unity.webrtc` 3.0.0-pre.8 or compatible) - pulled in by this package
3. **Socket.IO Unity** (required for Editor / Desktop / Mobile signaling). Add it from a Git URL: `https://github.com/itisnajim/SocketIOUnity.git`
    - Socket.IO Unity is not needed at runtime on WebGL (browser `socket.io-client` is bundled in the WebGL plugin).

## Install this transport

In Unity Package Manager, add a package from Git URL:

```
https://github.com/Unity-Technologies/multiplayer-community-contributions.git?path=/Transports/com.community.netcode.transport.webrtc
```

NOTE: Until this contribution is merged, you can use this fork URL:

```
https://github.com/aziztitu/unity-multiplayer-community-contributions.git?path=/Transports/com.community.netcode.transport.webrtc#transport/webrtc
```

After installing, add `WebRTCTransport` component on the same GameObject as `NetworkManager`.

To use the WebRTC Transport, assign it as the current `Network Transport` on the `NetworkManager` - either in the inspector, or in runtime before hosting/joining a session.

## Signaling server setup

WebRTC needs a signaling server to exchange offers, answers, and ICE candidates.

You can use the sample [webrtc-ngo-signaling](https://github.com/aziztitu/webrtc-ngo-signaling) server, or implement the same events on your own server.

The sample server listens on `http://localhost:4000` by default. If you set an `AUTH_TOKEN` on the server, use the same value on `WebRTCTransport.Signaling Server Auth Token`.

## Usage

1. Add `WebRTCTransport` to the same GameObject as your `NetworkManager`.
2. Set **Signaling Server URL**:
   - Local: `http://localhost:4000`
   - Production: `wss://your-domain.com`
3. Optionally set **Signaling Server Auth Token** to match the server.
4. Optionally add **Custom ICE Servers** (TURN).
5. Host: leave `roomId` empty. The server creates a room code and writes it back to `roomId`.
6. Client: set `roomId` to the host's code before calling `StartClient` / `NetworkManager.StartClient()`.

> Tip: During local testing, you can pre-fill the `roomId` to a custom value in the inspector, and all local instances will host/join the same room automatically.

### STUN / TURN

Built-in STUN servers:

- `stun:stun.l.google.com:19302`
- `stun:stun1.l.google.com:19302`
- `stun:stun2.l.google.com:19302`
- `stun:stun3.l.google.com:19302`
- `stun:stun.ekiga.net:3478`
- `stun:stun.iptel.org:3478`

**NOTE:** STUN-only connections can fail with symmetric NATs. Add at least one TURN server in **Custom ICE Servers** before going to production.

## WebGL

The WebGL JavaScript client is shipped as `Runtime/Plugins/WebGL/webrtc-web-client.jspre`, so you do **not** need to do anything extra.
