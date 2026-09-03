import { SignalClient } from './signal-client';
import { WebGLRtcPeerConnection } from './webgl-rtc-peer-connection';
import {
  UnityRTCIceConnectionState,
  WebRTCBridgeTypeConversions,
  type UnityIceServer,
  type UnityRTCSdpType,
  type UnityRTCSessionDescription,
} from './webrtc.bridge.types';

export class WebRTCBridge {
  bridgeGameObjectName = '';
  peerDataCallbackPtr?: number;

  signalClient?: SignalClient;
  peers: Record<number, WebGLRtcPeerConnection> = {};

  // Init
  public init(bridgeGameObjectName: string) {
    console.log('Initializing bridge: ', bridgeGameObjectName);
    this.bridgeGameObjectName = bridgeGameObjectName;
  }

  public registerPeerDataCallback(callbackPtr: number) {
    this.peerDataCallbackPtr = callbackPtr;
  }

  // Signalling
  public createSignalClient(url: string, token: string) {
    console.log('Creating new Signal Client', url);

    this.signalClient?.disconnect();
    this.signalClient = new SignalClient(url, token);
  }

  public async connectSignalClientAsync(callId: string): Promise<void> {
    try {
      await this.signalClient?.connect();
    } finally {
      this.notifyAsyncResult(callId);
    }
  }

  public disconnectSignalClient() {
    this.signalClient?.disconnect();
  }

  public sendSignal(eventName: string, payload: any) {
    this.signalClient?.emit(eventName, payload);
  }

  public listenForSignal(eventName: string) {
    this.signalClient?.on(eventName, (payload: object) => {
      const signalEvent = JSON.stringify({
        eventName,
        payload: JSON.stringify(payload),
      });
      window.SendMessage(this.bridgeGameObjectName, 'OnIncomingSignalEvent', signalEvent);
    });
  }

  // Peer Connection
  public createNewPeer(iceServers: UnityIceServer[], clientId: number): void {
    console.log('Creating new WebGLRtcPeerConnection', clientId);

    if (clientId in this.peers) {
      this.peers[clientId]?.close();
    }
    const peer = new WebGLRtcPeerConnection(iceServers, clientId);
    this.peers[clientId] = peer;
  }

  public isDataChannelOpen(clientId: number): boolean {
    const peer = this.getPeer(clientId);
    const isOpen = peer.isDataChannelOpen();
    console.log('Is Data Channel Open: ', clientId, isOpen);
    return isOpen;
  }

  public send(clientId: number, bytes: Uint8Array): void {
    console.log('Sending:', clientId, bytes.length);

    const peer = this.getPeer(clientId);
    peer.send(bytes);
  }

  public async setRemoteDescriptionAsync(
    clientId: number,
    description: UnityRTCSessionDescription,
    callId: string
  ): Promise<void> {
    try {
      const peer = this.getPeer(clientId);

      await peer.setRemoteDescription({
        type: WebRTCBridgeTypeConversions.RTCSdpType(description.type),
        sdp: description.sdp,
      });
    } finally {
      this.notifyAsyncResult(callId);
    }
  }

  public addIceCandidate(clientId: number, candidate: RTCIceCandidateInit): void {
    const peer = this.getPeer(clientId);
    peer.addIceCandidate(candidate);
  }

  public getIceConnectionState(clientId: number): UnityRTCIceConnectionState {
    const peer = this.getPeer(clientId);
    const state = peer.getIceConnectionState();
    return WebRTCBridgeTypeConversions.UnityRTCIceConnectionState(state);
  }

  public async prepareOfferAsync(clientId: number, callId: string): Promise<void> {
    try {
      const peer = this.getPeer(clientId);
      const offer = await peer.prepareOffer();
      this.notifyAsyncResult(
        callId,
        JSON.stringify(WebRTCBridgeTypeConversions.UnityRTCSessionDescription(offer))
      );
    } catch (err) {
      console.error('Error preparing offer', err);
      this.notifyAsyncResult(callId, JSON.stringify({}));
    }
  }

  public async prepareAnswerAsync(clientId: number, callId: string): Promise<void> {
    try {
      const peer = this.getPeer(clientId);
      const answer = await peer.prepareAnswer();
      this.notifyAsyncResult(
        callId,
        JSON.stringify(WebRTCBridgeTypeConversions.UnityRTCSessionDescription(answer))
      );
    } catch (err) {
      console.error('Error preparing answer', err);
      this.notifyAsyncResult(callId, JSON.stringify({}));
    }
  }

  public close(clientId: number): void {
    const peer = this.getPeer(clientId);
    peer.close();
  }

  public async getRTTAsync(clientId: number, callId: string): Promise<void> {
    try {
      const peer = this.getPeer(clientId);
      const rtt = await peer.getRTT();
      this.notifyAsyncResult(callId, JSON.stringify({ rtt: rtt }));
    } catch (error) {
      this.notifyAsyncResult(callId, JSON.stringify({ rtt: 0 }));
    }
  }

  private getPeer(clientId: number) {
    if (!(clientId in this.peers)) {
      throw `Cannot find peer connection for Client ID: ${clientId}`;
    }

    return this.peers[clientId];
  }

  // Helpers to send data to C#

  private notifyAsyncResult(callId: string, result: string = '') {
    const resultPayload = JSON.stringify({
      callId,
      result,
    });
    window.SendMessage(this.bridgeGameObjectName, 'OnJSAsyncResult', resultPayload);
  }

  public notifyPeerEvent(clientId: number, eventName: string, payload: object = {}) {
    const resultPayload = JSON.stringify({
      clientId,
      eventName,
      payload: JSON.stringify(payload),
    });
    window.SendMessage(this.bridgeGameObjectName, 'OnPeerEvent', resultPayload);
  }

  public forwardPeerData(clientId: number, bytes: Uint8Array) {
    if (!this.peerDataCallbackPtr) {
      console.error('Peer Data Callback is not registered');
      return;
    }

    const UnityModule = (window as any).UnityModule;
    console.log(UnityModule);

    // Allocating memory manually since ALLOC_STACK didn't work
    const ptr = (window as any)._malloc(bytes.length);
    UnityModule.HEAPU8.set(bytes, ptr);
    UnityModule.dynCall_viii(this.peerDataCallbackPtr, clientId, ptr, bytes.length);
    UnityModule._free(ptr);
  }
}
