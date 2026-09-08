import { WebRTCBridge } from "./webrtc/webrtc.bridge";

const webRtcBridge = new WebRTCBridge();

// Expose globally for Unity
(window as any).WebRTCBridge = webRtcBridge;
