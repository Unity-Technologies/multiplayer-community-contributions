export interface Room {
  host: string;
  clients: string[];
}

export interface HostRoomPayload {
  roomId?: string;
}

export interface HostRoomResponse {
  roomId: string;
}

export interface JoinRoomPayload {
  roomId: string;
}

export interface OfferPayload {
  to: string;
  sdp: string;
}

export interface AnswerPayload {
  to: string;
  sdp: string;
}

export interface CandidatePayload {
  to: string;
  candidate: object;
  roomId: string;
}
