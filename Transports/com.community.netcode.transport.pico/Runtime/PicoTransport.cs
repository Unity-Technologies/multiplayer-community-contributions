using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace Netcode.Transports.Pico
{
    [RequireComponent(typeof(NetworkManager))]
    public partial class PicoTransport : NetworkTransport, IRoomEventHandler
    {
        public enum EWorkMode
        {
            Simple = 0,
            ExternalRoom = 1
        }

        public enum ETransportEvent
        {
            Stopped = 0,
            Started
        }

        public enum EPicoServiceState
        {
            Stopped = 0,
            InRoom,
            RoomLeaving,
        }

        public EWorkMode WorkMode;
        private IRoomProvider _matchRoomProvider;
        public event Action<ETransportEvent> OnPicoTransportEvent;

        class PicoServiceInfo
        {
            public bool CancelFlag;
            public bool AllowHostMigrate = false;
            public bool IsHostMigrate;
            public ulong SelfUID;
            public ulong CurOwnerID;
            public EPicoServiceState CurState = EPicoServiceState.Stopped;
            public EPicoServiceState NotifiedState = EPicoServiceState.Stopped;
            public HashSet<ulong> CurRoomUIDs = new HashSet<ulong>();
            public void ResetServiceInfo()
            {
                CancelFlag = false;
                CurOwnerID = 0;
                SelfUID = 0;
                CurState = EPicoServiceState.Stopped;
                NotifiedState = EPicoServiceState.Stopped;
                CurRoomUIDs.Clear();
            }
        }

        private bool _netcodeStarted = false;
        private TransportRoomInfo _transportRoomInfo = null;
        private bool _isSelfServer = false;
        private readonly PicoServiceInfo _serviceInfo = new PicoServiceInfo();
        private ETransportEvent _transportState = ETransportEvent.Stopped;

        //used in stop procedure
        bool _roomLeftOnce = false;
        bool _stopTransportOnce = false;
        bool _afterShutdownOnce = false;

        private EPicoServiceState CurrentState
        {
            get => _serviceInfo.CurState;
            set => _serviceInfo.CurState = value;
        }

        void OnDestroy()
        {
            StopPicoTransport();
        }

        public bool IsHostMigrate()
        {
            return _serviceInfo.IsHostMigrate;
        }

        public void AllowHostMigrateOnHostLeave()
        {
            _serviceInfo.AllowHostMigrate = true;
        }

        public bool SetRoomProvider(IRoomProvider matchRoomProvider, ulong selfUID, TransportRoomInfo transportRoomInfo)
        {
            if (CurrentState != EPicoServiceState.Stopped)
            {
                PicoTransportLog(LogLevel.Error, $"duplicate SetRoomProvider, CurrentState: {CurrentState}");
                return false;
            }
            PicoTransportLog(LogLevel.Info, $"-------------------------------------------->");
            PicoTransportLog(LogLevel.Info, $"SetRoomProvider be called, CurrentState: {CurrentState}, isSelfOwner: {matchRoomProvider.IsSelfOwner()}");
            if (CurrentState != EPicoServiceState.Stopped && (!_serviceInfo.IsHostMigrate))
            {
                PicoTransportLog(LogLevel.Error, $"duplicated pico service init, CurrentState: {CurrentState}");
                return false;
            }
            _serviceInfo.IsHostMigrate = false;
            _serviceInfo.CancelFlag = false;
            if (matchRoomProvider != null)
            {
                _matchRoomProvider = matchRoomProvider;
            }
            if (null == matchRoomProvider)
            {
                PicoTransportLog(LogLevel.Error, "invalid matchroomProvider or invalid _initCallback");
                return false;
            }
            _serviceInfo.SelfUID = selfUID;
            _transportRoomInfo = transportRoomInfo;
            if (_netcodeStarted)
            {
                AfterNetcodeStarted();
            }
            return true;
        }

        private void AfterNetcodeStarted()
        {
            CurrentState = EPicoServiceState.InRoom;
            PicoTransportLog(LogLevel.Info, $"transport started, room {_transportRoomInfo.RoomID}");
            OnRoomInfoUpdate(_transportRoomInfo);
        }

        private void AfterShutdown()
        {
            if (!_netcodeStarted)
            {
                PicoTransportLog(LogLevel.Info, "!_netcodeStarted, ignore AfterShutdown");
                return;
            }
            if (!_serviceInfo.IsHostMigrate && _transportRoomInfo != null && 0 != _transportRoomInfo.RoomID && CurrentState == EPicoServiceState.InRoom)
            {
                PicoTransportLog(LogLevel.Info, $"AfterShutdown, issue self leave room {_transportRoomInfo.RoomID}, selfUID:{_serviceInfo.SelfUID}, ownerUID{_transportRoomInfo.OwnerUID}");
                CurrentState = EPicoServiceState.RoomLeaving;
                _matchRoomProvider.RoomLeave(_transportRoomInfo.RoomID);
            }
            if (_afterShutdownOnce)
            {
                return;
            }
            _afterShutdownOnce = true;
            PicoTransportLog(LogLevel.Info, "+AfterShutdown be called");
            _netcodeStarted = false;
            _joinRoomWaitGameInit = false;
            _transportRoomInfo = null;
            _isSelfServer = false;
            _transportState = ETransportEvent.Stopped;
            CurrentState = EPicoServiceState.Stopped;
            OnPicoTransportEvent?.Invoke(_transportState);
            _serviceInfo.ResetServiceInfo();
            _matchRoomProvider = null;
        }

        public void StopPicoTransport()
        {
            if (_stopTransportOnce)
            {
                return;
            }
            _stopTransportOnce = true;
            if (!NetworkManager.Singleton)
            {
                PicoTransportLog(LogLevel.Info, $"StopPicoTransport be called, ignore netcode shutdown, CurrentState {CurrentState}");
                AfterShutdown();
                return;
            }
            PicoTransportLog(LogLevel.Info, $"StopPicoTransport be called, CurrentState {CurrentState}");
            ShutdownNetcode();
        }

        void Update()
        {
            if (CurrentState == EPicoServiceState.Stopped)
            {
                return;
            }
            CheckNetcodeStartStopEvent(_serviceInfo.CurOwnerID, _serviceInfo.CurRoomUIDs);
            if (WorkMode == EWorkMode.Simple)
            {
                UpdateIndependent();
            }
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0;
            receiveTime = Time.realtimeSinceStartup;
            payload = default;
            return NetworkEvent.Nothing;
        }

        public override bool StartClient()
        {
            if (_netcodeStarted)
            {
                PicoTransportLog(LogLevel.Error, "duplicated netcode start, in StartClient");
                return false;
            }
            PicoTransportLog(LogLevel.Info, "---> StartClient");
            _netcodeStarted = true;
            if (WorkMode == EWorkMode.ExternalRoom)
            {
                if (_serviceInfo.SelfUID == _transportRoomInfo.OwnerUID)
                {
                    PicoTransportLog(LogLevel.Error, "self is room owner, but StartClient be called");
                    return false;
                }
            }
            _transportState = ETransportEvent.Started;
            OnPicoTransportEvent?.Invoke(_transportState);
            _isSelfServer = false;
            if (CurrentState == EPicoServiceState.Stopped)
            {
                if (WorkMode == EWorkMode.Simple)
                {
                    InitIndependent();
                } else
                {
                    AfterNetcodeStarted();
                }
                return true;
            }
            return true;
        }

        public override bool StartServer()
        {
            if (_netcodeStarted)
            {
                PicoTransportLog(LogLevel.Error, "duplicated netcode start, in StartServer");
                return false;
            }
            PicoTransportLog(LogLevel.Info, $"---> StartServer: current state({CurrentState})");
            _netcodeStarted = true;
            if (WorkMode == EWorkMode.ExternalRoom)
            {
                if (_serviceInfo.SelfUID != _transportRoomInfo.OwnerUID)
                {
                    PicoTransportLog(LogLevel.Error, $"self is not room owner, but StartServer be called, self {_serviceInfo.SelfUID} vs owner {_transportRoomInfo.OwnerUID}");
                    return false;
                }
            }
            _transportState = ETransportEvent.Started;
            OnPicoTransportEvent?.Invoke(_transportState);
            _isSelfServer = true;
            if (CurrentState == EPicoServiceState.Stopped)
            {
                if (WorkMode == EWorkMode.Simple)
                {
                    InitIndependent();
                }
                else
                {
                    AfterNetcodeStarted();
                }
                return true;
            }
            return true;
        }

        public override void DisconnectRemoteClient(ulong clientID)
        {
            if (!_isSelfServer)
            {
                PicoTransportLog(LogLevel.Error, "DisconnectRemoteClient, self is not server, skip this request");
                return;
            }
            if (_serviceInfo.AllowHostMigrate)
            {
                PicoTransportLog(LogLevel.Warn, "DisconnectRemoteClient, AllowHostMigrate, skip disconnect remote remoteUID:{clientID}");
                return;
            }
            PicoTransportLog(LogLevel.Info, "DisconnectRemoteClient, selfUID:{_serviceInfo.SelfUID}, ownerUID{_serviceInfo.OwnerUID}, remoteUID:{clientID}");
            if (CurrentState != EPicoServiceState.InRoom)
            {
                PicoTransportLog(LogLevel.Warn, $"DisconnectRemoteClient, CurrentState {CurrentState} not in game now, skip this request");
                return;
            }
            if (!_serviceInfo.CurRoomUIDs.Contains(clientID))
            {
                PicoTransportLog(LogLevel.Error, $"DisconnectRemoteClient, targetId({clientID}) is not in game now, skip this request");
                return;
            }
            _ = _matchRoomProvider.RoomKickUserByID(_transportRoomInfo.RoomID, clientID);
        }

        public override void DisconnectLocalClient()
        {
            PicoTransportLog(LogLevel.Info, $"DisconnectLocalClient, selfUID:{_serviceInfo.SelfUID}, ownerUID{_serviceInfo.CurOwnerID}");
            if (CurrentState != EPicoServiceState.InRoom)
            {
                PicoTransportLog(LogLevel.Info, $"DisconnectLocalClient, curState({CurrentState}), not in game now, skip this request");
                return;
            }
            if (!_serviceInfo.IsHostMigrate)
            {
                if ((_transportRoomInfo!=null) && (_transportRoomInfo.RoomID != 0))
                {
                    CurrentState = EPicoServiceState.RoomLeaving;
                    PicoTransportLog(LogLevel.Info, "DisconnectLocalClient, issue self leave room, selfUID:{_serviceInfo.SelfUID}, ownerUID{_serviceInfo.OwnerUID}");
                    if (!_matchRoomProvider.RoomLeave(_transportRoomInfo.RoomID))
                    {
                        PicoTransportLog(LogLevel.Info, "DisconnectLocalClient, issue self leave room failed, selfUID:{_serviceInfo.SelfUID}, ownerUID{_serviceInfo.OwnerUID}");
                    }
                }
            }
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            return 0;
        }

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            //PicoTransportLog(LogLevel.Debug, $"send be called, tgt clientId {clientId}, send size {data.Count}");
            byte[] dataArray = data.ToArray();
            if (clientId == ServerClientId)
            {
                clientId = _transportRoomInfo.OwnerUID;
            }
            _ = _matchRoomProvider.SendPacket2UID(clientId, dataArray);
        }

        public override ulong ServerClientId
        {
            get { return 0; }
        }

        // be called by NetworkManager
        public override void Shutdown()
        {
            PicoTransportLog(LogLevel.Info, "NetworkManager callback(Shutdown), selfId:{_serviceInfo.SelfUID}, curState: {CurrentState}");
            _serviceInfo.CancelFlag = true;
            if (CurrentState < EPicoServiceState.InRoom)
            {
                PicoTransportLog(LogLevel.Info, "Shutdown, selfId:{_serviceInfo.SelfUID}, curState: {CurrentState}, call UninitPicoService");
                AfterShutdown();
                return;
            }
            DisconnectLocalClient();
            AfterShutdown();
        }

        public override void Initialize(NetworkManager networkManager = null)
        {
            _roomLeftOnce = false;
            _stopTransportOnce = false;
            _afterShutdownOnce = false;

            _netcodeStarted = false;
            _joinRoomWaitGameInit = false;
            PicoTransportLog(LogLevel.Info, $"pico transport initialize be called, current LocalClientID {NetworkManager.Singleton.LocalClientId}");
            if (WorkMode == EWorkMode.Simple)
            {
                PrepareIndependent();
            }
        }

    } //PicoTransport
}
