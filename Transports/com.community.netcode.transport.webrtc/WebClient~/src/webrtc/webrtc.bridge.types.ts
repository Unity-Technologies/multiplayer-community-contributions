export type UnityIceServer = {
  username: string;
  credential: string;
  urls: string[];
};

export enum UnityRTCSdpType {
  Offer,
  Pranswer,
  Answer,
  Rollback,
}

export type UnityRTCSessionDescription = {
  sdp: string;
  type: UnityRTCSdpType;
};

export enum UnityRTCIceConnectionState {
  New = 0,
  Checking = 1,
  Connected = 2,
  Completed = 3,
  Failed = 4,
  Disconnected = 5,
  Closed = 6,
  Max = 7,
}

export const WebRTCBridgeTypeConversions = {
  RTCSdpType(i: UnityRTCSdpType): RTCSdpType {
    switch (i) {
      case UnityRTCSdpType.Answer:
        return 'answer';
      case UnityRTCSdpType.Offer:
        return 'offer';
      case UnityRTCSdpType.Pranswer:
        return 'pranswer';
      case UnityRTCSdpType.Rollback:
        return 'rollback';
    }
  },
  UnityRTCSdpType(i: RTCSdpType): UnityRTCSdpType {
    switch (i) {
      case 'answer':
        return UnityRTCSdpType.Answer;
      case 'offer':
        return UnityRTCSdpType.Offer;
      case 'pranswer':
        return UnityRTCSdpType.Pranswer;
      case 'rollback':
        return UnityRTCSdpType.Rollback;
    }
  },
  UnityRTCSessionDescription(i: RTCSessionDescriptionInit): UnityRTCSessionDescription {
    return {
      sdp: i.sdp ?? '',
      type: WebRTCBridgeTypeConversions.UnityRTCSdpType(i.type)
    }
  },
  UnityRTCIceConnectionState(i: RTCIceConnectionState): UnityRTCIceConnectionState {
    switch (i) {
      case 'new':
        return UnityRTCIceConnectionState.New;
      case 'checking':
        return UnityRTCIceConnectionState.Checking;
      case 'connected':
        return UnityRTCIceConnectionState.Connected;
      case 'completed':
        return UnityRTCIceConnectionState.Completed;
      case 'failed':
        return UnityRTCIceConnectionState.Failed;
      case 'disconnected':
        return UnityRTCIceConnectionState.Disconnected;
      case 'closed':
        return UnityRTCIceConnectionState.Closed;
    }
  },
};
