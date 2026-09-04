# WebRTC Transport for Netcode for GameObjects

A cross-platform [Netcode for GameObjects](https://docs-multiplayer.unity3d.com/) transport that uses WebRTC data channels. It supports Desktop, Mobile, and WebGL, including crossplay between those platforms.

Tested on Windows, Mobile, and WebGL with Netcode for GameObjects 2.4.2.

## Architecture

| Layer | Platform | Components |
| --- | --- | --- |
| C# | All | `WebRTCTransport`, platform-specific peer connections and signaling clients |
| Native WebRTC | Editor, Desktop, Mobile | [Unity WebRTC](https://docs.unity3d.com/Packages/com.unity.webrtc@3.0/manual/index.html) |
| WebGL | WebGL player | Packaged `.jslib` + `webrtc-web-client.jspre` (no custom WebGL template required) |
| Signaling | All | **Socket.IO** <br/> Public test server: [signal.multiplayer.azeesoft.com](https://signal.multiplayer.azeesoft.com/) <br/> Self-host for production: [webrtc-ngo-signaling](https://github.com/aziztitu/webrtc-ngo-signaling) |

## Dependencies

Install these in your Unity project before using the transport:

1. **Netcode for GameObjects** 2.0.0 or newer
2. **Unity WebRTC** (`com.unity.webrtc` 3.0.0-pre.8 or compatible) - pulled in by this package
3. **Socket.IO Unity** (required for Editor / Desktop / Mobile signaling). Add it from a Git URL: `https://github.com/itisnajim/SocketIOUnity.git`

## Install this transport

In Unity Package Manager, add a package from Git URL:

```
https://github.com/Unity-Technologies/multiplayer-community-contributions.git?path=/Transports/com.community.netcode.transport.webrtc
```

**NOTE:** Until this contribution is merged, you can use this fork URL instead:

```
https://github.com/aziztitu/unity-multiplayer-community-contributions.git?path=/Transports/com.community.netcode.transport.webrtc#transport/webrtc
```

After installing, add `WebRTCTransport` component on the same GameObject as `NetworkManager`.

To use the WebRTC Transport, assign it as the current `Network Transport` on the `NetworkManager` - either in the inspector, or in runtime before hosting/joining a session.

## Signaling server setup

WebRTC needs a signaling server to exchange offers, answers, and ICE candidates. Gameplay traffic does not go through that server.

### Development and testing

You can use the public signaling server available at [https://signal.multiplayer.azeesoft.com/](https://signal.multiplayer.azeesoft.com/) for development and testing.

- Open that link, and generate a token.

- Back in Unity, select the NetworkManager game object in the scene.

- Open the WebRTCTransport component in Inspector, and set these values:
   - **Signaling URL:** `wss://signal.multiplayer.azeesoft.com`
   - **Signaling Server Auth Token:** The generated token from the web page

### Production

For production, I recommend that you run your own signaling server. The open-source sample is [webrtc-ngo-signaling](https://github.com/aziztitu/webrtc-ngo-signaling) (clone, configure, and deploy it, or implement the same Socket.IO events on your own server).

## Usage

1. Add `WebRTCTransport` to the same GameObject as your `NetworkManager`.
2. Set **Signaling Server URL**:
   - Public test server: `wss://signal.multiplayer.azeesoft.com`
   - Local sample: `http://localhost:4000`
   - Your production server: `https://your-domain.com` (or `wss://your-domain.com`)
3. Set **Signaling Server Auth Token**:
   - Public test server: generate a token at [signal.multiplayer.azeesoft.com](https://signal.multiplayer.azeesoft.com/)
   - Your own server: match the token the server expects
4. Optionally add **Custom ICE Servers** (TURN).
   - Recommended for production
5. Host: leave `roomId` empty. The server creates a room code and writes it back to `roomId`.
6. Client: set `roomId` to the host's code before calling `StartClient` / `NetworkManager.StartClient()`.

> Tip: During local testing, you can pre-fill the `roomId` to a custom value in the inspector, and all local instances will host/join the same room automatically. This will help with iterating quickly.

### STUN / TURN

**Built-in STUN servers**

The package automatically includes these STUN servers, which should be good for development and testing:
- `stun:stun.l.google.com:19302`
- `stun:stun1.l.google.com:19302`
- `stun:stun2.l.google.com:19302`
- `stun:stun3.l.google.com:19302`
- `stun:stun.ekiga.net:3478`
- `stun:stun.iptel.org:3478`

> STUN-only connections can fail with symmetric NATs (Mobile Networks). So, before going to production, it is recommended that you add at least one TURN server in **Custom ICE Servers** in the Inspector.

## WebGL

The WebGL JavaScript client is shipped as `Runtime/Plugins/WebGL/webrtc-web-client.jspre`, so you do **not** need to do anything extra.
