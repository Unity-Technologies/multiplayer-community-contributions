using LiteNetLib;
using MLAPI.Logging;
using MLAPI.Transports;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace LiteNetLibTransport {

  public class LiteNetLibTransport : Transport, INetEventListener {

    class LiteChannel {
      public byte number;
      public DeliveryMethod method;
    }

    class Event {
      public NetEventType type;
      public ulong clientId;
      public string channelName;
      public NetPacketReader packetReader;
      public readonly DateTime dateTime;

      public Event() {
        dateTime = DateTime.UtcNow;
      }
    }

    [Tooltip("The port to listen on (if server) or connect to (if client)")]
    public ushort port = 7777;
    [Tooltip("The address to connect to as client; ignored if server")]
    public string address = "127.0.0.1";
    [Tooltip("Interval between ping packets used for detecting latency and checking connection, in seconds")]
    public float pingInterval = 1f;
    [Tooltip("Maximum duration for a connection to survive without receiving packets, in seconds")]
    public float disconnectTimeout = 5f;
    [Tooltip("Delay between connection attempts, in seconds")]
    public float reconnectDelay = 0.5f;
    [Tooltip("Maximum connection attempts before client stops and reports a disconnection")]
    public int maxConnectAttempts = 10;
    public List<TransportChannel> channels = new List<TransportChannel>();
    [Tooltip("Size of default buffer for decoding incoming packets, in bytes")]
    public int messageBufferSize = 1024 * 5;
    [Tooltip("Simulated chance for a packet to be \"lost\", from 0 (no simulation) to 100 percent")]
    public int simulatePacketLossChance = 0;
    [Tooltip("Simulated minimum additional latency for packets in milliseconds (0 for no simulation)")]
    public int simulateMinLatency = 0;
    [Tooltip("Simulated maximum additional latency for packets in milliseconds (0 for no simulation")]
    public int simulateMaxLatency = 0;

    private readonly ConcurrentDictionary<ulong, NetPeer> peers =
      new ConcurrentDictionary<ulong, NetPeer>();
    private readonly Dictionary<string, LiteChannel> liteChannels =
      new Dictionary<string, LiteChannel>();

    private NetManager netManager;
    private ConcurrentQueue<Event> eventQueue = new ConcurrentQueue<Event>();
    private ConcurrentBag<Event> eventPool = new ConcurrentBag<Event>();

    private byte[] messageBuffer;
    private WeakReference temporaryBufferReference;

    public override ulong ServerClientId => 0;
    private string hostType;
    private static readonly ArraySegment<byte> emptyArraySegment = new ArraySegment<byte>();

    private void OnValidate() {
      pingInterval = Math.Max(0, pingInterval);
      disconnectTimeout = Math.Max(0, disconnectTimeout);
      reconnectDelay = Math.Max(0, reconnectDelay);
      maxConnectAttempts = Math.Max(0, maxConnectAttempts);
      messageBufferSize = Math.Max(0, messageBufferSize);
      simulatePacketLossChance = Math.Min(100, Math.Max(0, simulatePacketLossChance));
      simulateMinLatency = Math.Max(0, simulateMinLatency);
      simulateMaxLatency = Math.Max(simulateMinLatency, simulateMaxLatency);
    }

    public override void Send(ulong clientId, ArraySegment<byte> data, string channelName, bool skipQueue) {
      if (LogHelper.CurrentLogLevel <= LogLevel.Developer) LogHelper.LogInfo("Sending message of " + data.Count + " bytes to " + clientId + " on " + channelName);
      var channel = liteChannels[channelName];
      peers.TryGetValue(clientId, out NetPeer peer);
      peer.Send(data.Array, data.Offset, data.Count, channel.number, channel.method);
      if (skipQueue) peer.Flush();
    }

    public override void FlushSendQueue(ulong clientId) {
      if (peers.TryGetValue(clientId, out NetPeer peer)) peer.Flush();
    }

    public override NetEventType PollEvent(out ulong clientId, out string channelName, out ArraySegment<byte> payload) {
      clientId = 0;
      channelName = null;
      payload = emptyArraySegment;
      return NetEventType.Nothing;
    }

    public override NetEventType PollEvent(out ulong clientId, out string channelName, out ArraySegment<byte> payload, out float receiveTime) {
      payload = emptyArraySegment;
      if (!eventQueue.TryDequeue(out Event ev)) {
        clientId = 0;
        channelName = null;
        receiveTime = 0;
        return NetEventType.Nothing;
      }

      clientId = ev.clientId;
      channelName = ev.channelName;
      receiveTime = Time.realtimeSinceStartup - ((float) DateTime.UtcNow.Subtract(ev.dateTime).TotalSeconds);

      if (ev.packetReader != null) {
        var size = ev.packetReader.UserDataSize;
        var data = messageBuffer;
        if (size > messageBuffer.Length) {
          if (temporaryBufferReference != null && temporaryBufferReference.IsAlive &&
              ((byte[]) temporaryBufferReference.Target).Length >= size) {
            data = (byte[]) temporaryBufferReference.Target;
          } else {
            data = new byte[size];
            temporaryBufferReference = new WeakReference(data);
          }
        }
        Array.Copy(ev.packetReader.RawData, ev.packetReader.UserDataOffset, data, 0, size);
        payload = new ArraySegment<byte>(data, 0, size);
        ev.packetReader.Recycle();
      }

      if (LogHelper.CurrentLogLevel <= LogLevel.Developer) {
        LogHelper.LogInfo("Deliver event " + ev.type + " to " + clientId);
      }

      return ev.type;
    }

    public override void StartClient() {
      if (hostType != null) throw new Exception("Already started as " + hostType);
      hostType = "client";
      netManager.Start();
      var peer = netManager.Connect(address, port, "");
      if (peer.Id != 0) throw new Exception("Server peer did not have id 0: " + peer.Id);
      peers[(ulong) peer.Id] = peer;
    }

    public override void StartServer() {
      if (hostType != null) throw new Exception("Already started as " + hostType);
      hostType = "server";
      netManager.Start(port);
    }

    public override void DisconnectRemoteClient(ulong clientId) {
      if (peers.TryGetValue(clientId, out NetPeer peer)) peer.Disconnect();
    }

    public override void DisconnectLocalClient() {
      netManager.Flush();
      netManager.DisconnectAll();
      peers.Clear();
    }

    public override ulong GetCurrentRtt(ulong clientId) {
      if (!peers.ContainsKey(clientId)) return 0;
      return (ulong) peers[clientId].Ping * 2;
    }

    public override void Shutdown() {
      netManager.Flush();
      netManager.Stop();
      peers.Clear();
      hostType = null;
    }

    public override void Init() {
      liteChannels.Clear();
      MapChannels(MLAPI_CHANNELS);
      MapChannels(channels);
      AddRpcResponseChannels();
      if (liteChannels.Count > 64) {
        throw new Exception("LiteNetLib supports up to 64 channels, got: " + liteChannels.Count);
      }
      messageBuffer = new byte[messageBufferSize];
      netManager = new NetManager(this) {
        UnsyncedEvents = true,
        ChannelsCount = (byte) liteChannels.Count,
        PingInterval = s2ms(pingInterval),
        DisconnectTimeout = s2ms(disconnectTimeout),
        ReconnectDelay = s2ms(reconnectDelay),
        MaxConnectAttempts = maxConnectAttempts,
        SimulatePacketLoss = simulatePacketLossChance > 0,
        SimulationPacketLossChance = simulatePacketLossChance,
        SimulateLatency = simulateMaxLatency > 0,
        SimulationMinLatency = simulateMinLatency,
        SimulationMaxLatency = simulateMaxLatency
      };
    }

    private void MapChannels(IEnumerable<TransportChannel> list) {
      var id = (byte) liteChannels.Count;
      foreach (var channel in list) {
        liteChannels.Add(
          channel.Name, new LiteChannel { number = id++, method = ConvertChannelType(channel.Type) });
      }
    }

    private void AddRpcResponseChannels() {
      var id = (byte) liteChannels.Count;
      foreach (var method in Enum.GetValues(typeof(DeliveryMethod)) as DeliveryMethod[]) {
        liteChannels.Add(
          "LITENETLIB_RESPONSE_" + method.ToString(),
          new LiteChannel { number = id++, method = method }
        );
      }
    }

    private DeliveryMethod ConvertChannelType(ChannelType type) {
      switch (type) {
        case ChannelType.Unreliable: {
            return DeliveryMethod.Unreliable;
        }
        case ChannelType.UnreliableSequenced: {
            return DeliveryMethod.Sequenced;
        }
        case ChannelType.Reliable: {
            return DeliveryMethod.ReliableUnordered;
        }
        case ChannelType.ReliableSequenced: {
            return DeliveryMethod.ReliableOrdered;
        }
        case ChannelType.ReliableFragmentedSequenced: {
            return DeliveryMethod.ReliableOrdered;
        }
        default: {
          throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
      }
    }

    void INetEventListener.OnPeerConnected(NetPeer peer) {
      if (LogHelper.CurrentLogLevel <= LogLevel.Developer) {
        LogHelper.LogInfo("Peer connected " + peer.Id);
      }
      if (!eventPool.TryTake(out Event ev)) ev = new Event();
      ev.type = NetEventType.Connect;
      ev.clientId = peerToClientId(peer);
      peers[ev.clientId] = peer;
      eventQueue.Enqueue(ev);
    }

    void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) {
      if (LogHelper.CurrentLogLevel <= LogLevel.Developer) {
        LogHelper.LogInfo("Peer disconnected " + peer.Id);
      }
      if (!eventPool.TryTake(out Event ev)) ev = new Event();
      ev.type = NetEventType.Disconnect;
      ev.clientId = peerToClientId(peer);
      peers.TryRemove(ev.clientId, out _);
      eventQueue.Enqueue(ev);
    }

    void INetEventListener.OnNetworkError(IPEndPoint endPoint, SocketError socketError) {
      if (LogHelper.CurrentLogLevel <= LogLevel.Error) {
        LogHelper.LogError("Network error: " + socketError.ToString());
      }
    }

    void INetEventListener.OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod deliveryMethod) {
      if (LogHelper.CurrentLogLevel <= LogLevel.Developer) {
        LogHelper.LogInfo("Received packet from peer " + peer.Id);
      }
      if (!eventPool.TryTake(out Event ev)) ev = new Event();
      ev.type = NetEventType.Data;
      ev.clientId = peerToClientId(peer);
      ev.packetReader = reader;
      ev.channelName = "LITENETLIB_RESPONSE_" + deliveryMethod.ToString();
      eventQueue.Enqueue(ev);
    }

    void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) {
      // Ignore
      if (LogHelper.CurrentLogLevel <= LogLevel.Developer) {
        LogHelper.LogInfo("Received unconnected message");
      }
    }

    void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency) {
      // Ignore
    }

    void INetEventListener.OnConnectionRequest(ConnectionRequest request) {
      if (LogHelper.CurrentLogLevel <= LogLevel.Developer) {
        LogHelper.LogInfo("Accepting connection request from peer " + request.Peer.Id);
      }
      request.Accept();
    }

    private ulong peerToClientId(NetPeer peer) {
      var clientId = (ulong) peer.Id;
      if (hostType == "server") clientId += 1;
      return clientId;
    }

    private static int s2ms(float s) {
      return (int) Mathf.Ceil(s * 1000);
    }
  }
}
