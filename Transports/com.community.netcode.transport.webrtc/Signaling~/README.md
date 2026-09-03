# WebRTC Signaling Server

Sample Socket.IO signaling server for the WebRTC Netcode for GameObjects transport.

```bash
npm install
cp .env.example .env
npm run serve
```

Listens on `http://localhost:4000` by default. Set `AUTH_TOKEN` in `.env` and on `WebRTCTransport` if you want handshake authentication.
