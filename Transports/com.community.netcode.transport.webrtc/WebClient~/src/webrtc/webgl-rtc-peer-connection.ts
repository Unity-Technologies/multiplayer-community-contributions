import { WebRTCBridgeTypeConversions } from './webrtc.bridge.types';

export class WebGLRtcPeerConnection {
  peer: RTCPeerConnection;
  dataChannel?: RTCDataChannel;

  constructor(iceServers: RTCIceServer[], private readonly clientId: number) {
    this.peer = new RTCPeerConnection({
      iceServers: iceServers,
    });

    this.peer.onicecandidate = (e) => {
      const cand = e.candidate;
      if (!cand) {
        return;
      }

      window.WebRTCBridge.notifyPeerEvent(this.clientId, 'onicecandidate', {
        candidate: cand.candidate,
        sdpMid: cand.sdpMid,
        sdpMLineIndex: cand.sdpMLineIndex,
      });
    };

    this.peer.oniceconnectionstatechange = () => {
      window.WebRTCBridge.notifyPeerEvent(this.clientId, 'oniceconnectionstatechange', {
        state: WebRTCBridgeTypeConversions.UnityRTCIceConnectionState(this.peer.iceConnectionState),
      });
    };

    console.log('Initializing Data Channel Logic...');
    this.initDataChannel();
  }

  private initDataChannel() {
    if (this.isHost()) {
      this.dataChannel = this.peer.createDataChannel('game');
      this.setupDataChannelEvents();
    } else {
      this.peer.ondatachannel = (e) => {
        console.log(
          `[WebRTCTransport] OnDataChannel available: ${e.channel.id}, ${e.channel.negotiated}, ${e.channel.readyState}`
        );

        this.dataChannel = e.channel;

        // This additional check is needed on the Desktop version, but this creates a duplicate event here
        // if (this.dataChannel.readyState == 'open') {
        //   console.log('[WebRTCTransport] Client DataChannel is already open');
        //   window.WebRTCBridge.notifyPeerEvent(this.clientId, 'datachannel.onopen');
        // }

        this.setupDataChannelEvents();
      };
    }
  }

  private setupDataChannelEvents() {
    if (!this.dataChannel) {
      return;
    }

    this.dataChannel.onopen = () => {
      window.WebRTCBridge.notifyPeerEvent(this.clientId, 'datachannel.onopen');
    };

    this.dataChannel.onmessage = (messageEvent) => {
      const bytes = new Uint8Array(messageEvent.data);
      window.WebRTCBridge.forwardPeerData(this.clientId, bytes);
    };

    this.dataChannel.onerror = (errorEvent) => {
      const error = errorEvent.error;
      console.error(
        `Error from data channel on client ${this.clientId}`,
        error.errorDetail,
        error.message
      );

      window.WebRTCBridge.notifyPeerEvent(this.clientId, 'datachannel.onerror', {
        message: error.message,
      });
    };
  }

  public isDataChannelOpen() {
    if (this.dataChannel == null) {
      return false;
    }

    return this.dataChannel.readyState == 'open';
  }

  public send(bytes: Uint8Array) {
    console.log(bytes);

    this.dataChannel?.send(bytes);
  }

  public async setRemoteDescription(description: RTCSessionDescriptionInit) {
    await this.peer.setRemoteDescription(description);
  }

  public addIceCandidate(candidate: RTCIceCandidateInit) {
    this.peer.addIceCandidate(candidate);
  }

  public getIceConnectionState() {
    return this.peer.iceConnectionState;
  }

  public async prepareOffer(): Promise<RTCSessionDescriptionInit> {
    var offer = await this.peer.createOffer();
    await this.peer.setLocalDescription(offer);
    return offer;
  }

  public async prepareAnswer(): Promise<RTCSessionDescriptionInit> {
    var answer = await this.peer.createAnswer();
    await this.peer.setLocalDescription(answer);
    return answer;
  }

  public close() {
    this.peer?.close();
    this.dataChannel?.close();
  }

  public async getRTT(): Promise<number> {
    var statsReport = await this.peer.getStats();

    const allStats: RTCStats[] = [];
    statsReport.forEach((stat: RTCStats) => {
      allStats.push(stat);
    });

    for (const stat of allStats) {
      if (stat.type == 'candidate-pair') {
        var candidatePair = stat as RTCIceCandidatePairStats;
        if (candidatePair != null && candidatePair.currentRoundTripTime) {
          return candidatePair.currentRoundTripTime;
        }
      }
    }

    return 0; // RTT not available yet
  }

  private isHost() {
    return this.clientId !== 0;
  }
}
