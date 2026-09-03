import type { WebRTCBridge } from '../webrtc/webrtc.bridge';

declare global {
  interface Window {
    SendMessage: (gameObject: string, methodName: string, message: string) => void;
    WebRTCBridge: WebRTCBridge;
  }

  declare var Module: {
    HEAPU8: Uint8Array;
    dynCall_viii: (fnPtr: number, clientId: number, ptr: number, len: number) => void;
  };
}

export {};
