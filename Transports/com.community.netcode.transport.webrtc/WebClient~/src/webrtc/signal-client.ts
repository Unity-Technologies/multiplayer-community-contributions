import { io, Socket } from 'socket.io-client';

/**
 * Just a wrapper for socket.io-client
 */
export class SignalClient {
  socket: Socket;

  constructor(private readonly url: string, private readonly token: string) {
    this.socket = io(this.url, {
      autoConnect: false,
      auth: {
        token: this.token,
      },
      transports: ['websocket'],
    });
    console.log('Socket IO client created');
  }

  public async connect(): Promise<void> {
    return new Promise((resolve, reject) => {
      const onConnect = () => {
        this.socket.off("connect_error", onError);
        resolve();
      };
      const onError = (err: any) => {
        this.socket.off("connect", onConnect);
        reject(err);
      };

      this.socket.once("connect", onConnect);
      this.socket.once("connect_error", onError);

      this.socket.connect(); // start connecting
    });
  }

  public disconnect() {
    if (!this.socket.connected) {
      return;
    }
    this.socket.disconnect();
  }

  public emit(eventName: string, ...payload: any[]) {
    this.socket.emit(eventName, ...payload);
  }

  public on(eventName: string, listener: (...args: any[]) => void) {
    this.socket.on(eventName, listener);
  }
}
