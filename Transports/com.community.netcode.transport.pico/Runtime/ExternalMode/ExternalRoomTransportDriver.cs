using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using PicoPlatform = Pico.Platform;

namespace Netcode.Transports.Pico
{
    public partial class ExternalRoomTransportDriver
    {
        public enum ETransportDriverEvent
        {
            Error,
            Stopped,
            BeforeReenter,
            AfterReenter,
        }
        public event Action<NetworkEvent, ulong> OnClientEvent;
        public event Action<ETransportDriverEvent, int, string> OnDriverEvent;

        private string _selfOpenID;
        private bool _inited;
        private TransportPicoRoomInfo _picoRoomWrapper;
        private PicoTransport _picoTransport;
        private Dictionary<ulong, string> _networkid2OpenID;
        private bool _restartFlag;

        public bool Init(bool autoRestartNetcode, PicoTransport picoTransport, string selfOpenID, PicoPlatform.Models.Room roomInfo)
        {
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"Init transport driver with autoRestartNetcode({autoRestartNetcode}), selfOpenID({selfOpenID}), roomID({roomInfo.RoomId})");
            _selfOpenID = selfOpenID;
            _picoTransport = picoTransport;
            _restartFlag = false;
            if (autoRestartNetcode)
            {
                _picoTransport.AllowHostMigrateOnHostLeave();
            }
            _networkid2OpenID = new Dictionary<ulong, string>();
            if (roomInfo.OwnerOptional == null)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "owner is not in room now, postpone transport init ...");
                _inited = false;
                return true;
            }            
            return InnerStart(selfOpenID, TransportPicoRoomInfo.GetPicoRoomInfo(roomInfo));
        }

        public void Uninit(string reason)
        {
            if (!_inited)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"pico transport driver has already stopped, skip(reason: {reason})");
                return;
            }
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"Uninit transport driver now(reason: {reason}) ...");
            _inited = false;
            _picoTransport.StopPicoTransport();
            if (!_picoTransport.IsHostMigrate())
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "Uninit transport driver reset _picoTransport");
                _selfOpenID = null;
                _picoTransport = null;
                if (NetworkManager.Singleton)
                {
                    NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnectedCallback;
                    NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnectedCallback;
                }
            }
        }

        private void HandleRoomEvent(TransportPicoRoomInfo.ERoomEvent roomEvent, string eventDesc, PicoRoomInfo roomInfo)
        {
            switch (roomEvent)
            {
                case TransportPicoRoomInfo.ERoomEvent.OwnerLeaveRoom:
                case TransportPicoRoomInfo.ERoomEvent.SelfLeaveRoom:
                case TransportPicoRoomInfo.ERoomEvent.UpdateRoomInfo:
                    DriverRoomInfoUpdate(roomInfo);
                    break;
            }
            

            return;
        }

        private bool InnerStart(string selfOpenID, PicoRoomInfo roomInfo)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnectedCallback;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnectedCallback;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedCallback;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectedCallback;
            _picoRoomWrapper = new TransportPicoRoomInfo();
            _picoRoomWrapper.InitWithPicoRoomInfo(selfOpenID, roomInfo);
            _picoRoomWrapper.OnRoomEvent += HandleRoomEvent;
            _picoTransport.OnPicoTransportEvent -= HandlePicoTransportEvent;
            _picoTransport.OnPicoTransportEvent += HandlePicoTransportEvent;
            
            _networkid2OpenID = new Dictionary<ulong, string>();
            if (!InitRoomProvider(_picoRoomWrapper))
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, "init picoProvider failed");
                return false;
            }
            bool isServer = IsSelfOwner();
            _picoTransport.SetRoomProvider(this, GetSelfLoggedInUID(), GetTransportPicoRoomInfo());
            {
                NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
                if (isServer)
                {
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"!!! call Netcode's StartHost, selfOpenID {_selfOpenID}");
                    NetworkManager.Singleton.ConnectionApprovalCallback = ConnectionApprovalCallback;
                    NetworkManager.Singleton.StartHost();
                }
                else
                {
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"!!! call Netcode's StartClient, selfOpenID {_selfOpenID}");
                    NetworkManager.Singleton.NetworkConfig.ConnectionData = System.Text.Encoding.ASCII.GetBytes(_selfOpenID);
                    NetworkManager.Singleton.StartClient();
                }
            };
            _inited = true;
            return true;
        }

        public string GetOpenIDOfNetworkID(ulong networkID)
        {
            string openID = "";
            bool got = false;
            if (NetworkManager.Singleton.IsServer)
            {
                got = _networkid2OpenID.TryGetValue(networkID, out openID);
            }            
            return got ? openID : "only valid in server";
        }

        public void DriverRoomInfoUpdate(PicoRoomInfo roomInfo)
        {
            if (roomInfo.RoomID == 0)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "OnRoomInfoUpdate, room_id is 0(leave room notification)");
                Uninit("room left");
                return;
            }
            if (!_inited)
            {
                if (roomInfo.OwnerOpenID.Length<=0)
                {
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "owner is not in room now, postpone (again) transport init ...");
                    return;
                }
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, "owner entered room now, issue transport init ...");
                InnerStart(_selfOpenID, roomInfo);
                return;
            }
            _picoRoomWrapper.PicoRoomInfo = roomInfo;
            _status.PicoRoomWrapper.PicoRoomInfo = roomInfo;
            _status.ParseUIDInfo();
            _picoTransport.OnRoomInfoUpdate(GetTransportPicoRoomInfo());
        }
        
        private void HandleMsgFromRoom(PicoPlatform.Models.Packet pkgFromRoom)
        {
            byte[] message = new byte[pkgFromRoom.Size];
            ulong pkgSize = pkgFromRoom.GetBytes(message);
            if (pkgSize <= 0)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, $"OnMsgFromRoom, error pkgSize: {pkgSize}");
                OnDriverEvent?.Invoke(ETransportDriverEvent.Error, -1, "error pkg size from room");
                return;
            }
            HandleMsgFromRoom(pkgFromRoom.SenderId, message);
        }        

        private void OnClientConnectedCallback(ulong clientID)
        {
            string openID;
            if (NetworkManager.Singleton.IsServer)
            {
                bool gotOpenID = _networkid2OpenID.TryGetValue(clientID, out openID);
                if (!gotOpenID)
                {
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Warn, $"OnClientConnectedCallback, unknown client, its netcode ID {clientID}");
                } else
                {
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"OnClientConnectedCallback, clientID {clientID}, clientOpenID {openID}");
                }
            }
            OnClientEvent?.Invoke(NetworkEvent.Connect, clientID);
        }

        private void OnClientDisconnectedCallback(ulong clientID)
        {
            string openID;
            if (NetworkManager.Singleton.IsServer)
            {
                bool gotOpenID = _networkid2OpenID.TryGetValue(clientID, out openID);
                if (!gotOpenID)
                {
                    //will get here only if the pico room player who has not finished the approval procedure leaves now.
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Warn, $"OnClientDisconnectedCallback, unknown client, its netcode ID {clientID}");
                } else
                {
                    PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"OnClientDisconnectedCallback, clientID {clientID}, clientOpenID {openID}");
                }
            }
            OnClientEvent?.Invoke(NetworkEvent.Disconnect, clientID);
        }

        public void Update()
        {
            CheckRestartNetcode();
            RecvRoomPackage();
        }

        private bool CheckRestartNetcode()
        {
            if (!_restartFlag)
            {
                return false;
            }
            _restartFlag = false;
            if (_picoTransport == null || !(_picoTransport.IsHostMigrate()))
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Error, $"CheckRestartNetcode, _picoTransport:{_picoTransport}, isHostMigrated:{_picoTransport && _picoTransport.IsHostMigrate()}");
                return false;
            }
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"CheckRestartNetcode, got restart flag, reinit picoTransport ...");
            OnDriverEvent?.Invoke(ETransportDriverEvent.BeforeReenter, 0, "");
            InnerStart(_picoRoomWrapper.SelfOpenID, _picoRoomWrapper.PicoRoomInfo);
            OnDriverEvent?.Invoke(ETransportDriverEvent.AfterReenter, 0, "");
            return true;
        }

        private void RecvRoomPackage()
        {
            // read packet
            var packet = PicoPlatform.NetworkService.ReadPacket();
            while (packet != null)
            {
                HandleMsgFromRoom(packet);
                packet.Dispose();
                packet = PicoPlatform.NetworkService.ReadPacket();
            }
        }
        
        private void ConnectionApprovalCallback(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            var playerOpenID = System.Text.Encoding.ASCII.GetString(request.Payload);
            if (NetworkManager.ServerClientId == request.ClientNetworkId)
            {
                PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"!!!, ClientID(host) {request.ClientNetworkId} pre approval, use m_selfOpenID {_selfOpenID}!");
                playerOpenID = _selfOpenID;
            }
            _networkid2OpenID.Add(request.ClientNetworkId, playerOpenID);
            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"!!!, ClientID {request.ClientNetworkId} start approval, It's openID {playerOpenID}!");
            response.Approved = true;
            response.CreatePlayerObject = NetworkManager.Singleton.NetworkConfig.PlayerPrefab != null;
        }

        private void HandlePicoTransportEvent(PicoTransport.ETransportEvent transportState)
        {
            switch (transportState)
            {
                case PicoTransport.ETransportEvent.Stopped:
                    {
                        if (_picoTransport != null && _picoTransport.IsHostMigrate())
                        {
                            PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Warn, $"transport will be restart, skip Stop event notification");
                            _restartFlag = true;
                            return;
                        } 
                        PicoTransport.PicoTransportLog(PicoTransport.LogLevel.Info, $"driver go event: pico transport shutdown");
                        OnDriverEvent?.Invoke(ETransportDriverEvent.Stopped, 0, "transport stopped");
                    }
                    break;
                default:
                    break;
            }
            return;
        }
    }
}