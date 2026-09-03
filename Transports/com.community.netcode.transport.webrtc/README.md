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
2. **Unity WebRTC** (`com.unity.webrtc` 3.0.0-pre.8 or compatible) — pulled in by this package
3. **Socket.IO Unity** (required for Editor / Desktop / Mobile signaling). Add it from a Git URL:

```
https://github.com/itisnajim/SocketIOUnity.git
```

This package cannot declare that Git dependency itself. Socket.IO Unity is not needed at runtime on WebGL (browser `socket.io-client` is bundled in the WebGL plugin).

## Install this transport

In Unity Package Manager, add a package from Git URL:

```
https://github.com/Unity-Technologies/multiplayer-community-contributions.git?path=/Transports/com.community.netcode.transport.webrtc
```

Until this contribution is merged, use the fork URL:

```
https://github.com/aziztitu/unity-multiplayer-community-contributions.git?path=/Transports/com.community.netcode.transport.webrtc
```

After installing, set Network Transport to None on your `NetworkManager`, then choose **WebRTC Transport** from the Select Transport dropdown. You can also add `WebRTCTransport` on the same GameObject as `NetworkManager` and assign it manually.

## Signaling server

WebRTC needs a signaling server to exchange offers, answers, and ICE candidates.

You can use the sample [webrtc-ngo-signaling](https://github.com/aziztitu/webrtc-ngo-signaling) server, or any server that implements these events:
`host-room`, `room-created`, `host-room-failed`, `join-room`, `room-not-found`, `new-client`, `offer`, `answer`, `candidate`, `client-disconnected`, `host-disconnected`.

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

### STUN / TURN

Built-in STUN servers:

- `stun:stun.l.google.com:19302`
- `stun:stun1.l.google.com:19302`
- `stun:stun2.l.google.com:19302`
- `stun:stun3.l.google.com:19302`
- `stun:stun.ekiga.net:3478`
- `stun:stun.iptel.org:3478`

STUN-only connections can fail with symmetric NATs. Add at least one TURN server in **Custom ICE Servers** before going to production.

## WebGL

The WebGL JavaScript client is shipped as `Runtime/Plugins/WebGL/webrtc-web-client.jspre`, so you do **not** need a custom WebGL template.

To rebuild the browser bundle from TypeScript:

```bash
cd WebClient~
npm install
npm run build
```

`WebClient~/vite.config.js` writes the IIFE bundle to `Runtime/Plugins/WebGL/webrtc-web-client.jspre`.
