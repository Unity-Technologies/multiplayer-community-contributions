import { defineConfig } from 'vite';

export default defineConfig({
  build: {
    lib: {
      entry: 'src/main.ts',
      formats: ['iife'],
      name: 'WebRTCBridgeModule',
      fileName: () => 'webrtc-web-client.jspre'
    },
    outDir: '../Runtime/Plugins/WebGL',
    emptyOutDir: false,
    minify: true
  }
});
