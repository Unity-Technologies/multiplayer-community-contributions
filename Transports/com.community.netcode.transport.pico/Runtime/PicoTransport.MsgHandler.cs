using System;
using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

namespace Netcode.Transports.Pico
{
    public partial class PicoTransport
    {
        private void InvokePicoTransportEvent(NetworkEvent networkEvent, ulong userId = 0, ArraySegment<byte> payload = default)
        {
            if (networkEvent != NetworkEvent.Data)
            {
                PicoTransportLog(LogLevel.Info, $"InvokePicoTransportEvent, networkEvent:{networkEvent}, userId:{userId}");
            }
            else
            {
                //PicoTransportLog(LogLevel.Info, $"network payload recved: from userId({userId}), size({payload.Count})");
            }
            switch (networkEvent)
            {
                case NetworkEvent.Nothing:
                    // do nothing
                    break;
                case NetworkEvent.Disconnect:
                    // need no extra handling now
                    goto default;
                default:
                    InvokeOnTransportEvent(networkEvent, userId, payload, Time.realtimeSinceStartup);
                    break;
            }
        }

        private void TryIssueTransoprtEvent(NetworkEvent networkEvent, ulong uid)
        {
            bool isSelf = uid == _serviceInfo.SelfUID;
            bool isServerEvent = uid == _transportRoomInfo.OwnerUID || (uid == 0);
            if (_isSelfServer)
            {
                if (!isSelf)
                {
                    PicoTransportLog(LogLevel.Info, $"(server) send others player event '{networkEvent}' to netcode, uid in event:{uid}, ownerUID:{_transportRoomInfo.OwnerUID}");
                    InvokePicoTransportEvent(networkEvent, uid);
                }
            }
            else
            {
                if (isSelf)
                {
                    PicoTransportLog(LogLevel.Warn, $"(client) send self event '{networkEvent}' to netcode, uid in event:{uid}, ownerUID:{_transportRoomInfo.OwnerUID}");
                    InvokePicoTransportEvent(networkEvent, uid);
                }
                else if (isServerEvent)
                {
                    PicoTransportLog(LogLevel.Info, $"(client) send server's event '{networkEvent}' to netcode, uid in event:{uid}, ownerUID:{_transportRoomInfo.OwnerUID}");
                    InvokePicoTransportEvent(networkEvent, uid);
                }
            }
        }

        private bool CheckNetcodeStartStopEvent(ulong curOwnerID, HashSet<ulong> newestRoomUIDs)
        {
            if (_transportState != ETransportEvent.Started)
            {
                //wait StartClient/StartHost to be called
                return false;
            }
            EPicoServiceState notied_state = _serviceInfo.NotifiedState;
            EPicoServiceState cur_state = _serviceInfo.CurState;
            if (notied_state == cur_state)
            {
                return false;

            }
            PicoTransportLog(LogLevel.Info, $"notied_state {notied_state} disaccord with cur_state {cur_state}");
            if (cur_state == EPicoServiceState.InRoom)
            {
                //in room, and haven't notified netcode yet.
                PicoTransportLog(LogLevel.Info, $"self new enter room, self uid:{_serviceInfo.SelfUID}, serverUID:{_transportRoomInfo.OwnerUID}");
                if (!_isSelfServer)
                {
                    //non-server, notify(server online)
                    PicoTransportLog(LogLevel.Info, $"(clietn) self is client: try notify server connect event, self uid:{_serviceInfo.SelfUID}, serverUID:{_transportRoomInfo.OwnerUID}");
                    TryIssueTransoprtEvent(NetworkEvent.Connect, _transportRoomInfo.OwnerUID);
                }
                else
                {
                    //server, notify(other clients online)
                    foreach (ulong uid in newestRoomUIDs)
                    {
                        PicoTransportLog(LogLevel.Info, $"(server) self is server: try notify client connect event, client uid:{uid}, serverUID:{_transportRoomInfo.OwnerUID}");
                        if (uid != _serviceInfo.SelfUID)
                        {
                            TryIssueTransoprtEvent(NetworkEvent.Connect, uid);
                        }
                    }
                }
            }
            else
            {
                //not in room now
                if (notied_state == EPicoServiceState.InRoom)
                {
                    PicoTransportLog(LogLevel.Info, $"self disconnect, self uid:{_serviceInfo.SelfUID}, serverUID:{_transportRoomInfo.OwnerUID}");
                    TryIssueTransoprtEvent(NetworkEvent.Disconnect, _transportRoomInfo.OwnerUID);
                }
            }
            _serviceInfo.NotifiedState = cur_state;
            _serviceInfo.CurOwnerID = curOwnerID;
            _serviceInfo.CurRoomUIDs = new HashSet<ulong>(newestRoomUIDs);
            return true;
        }

        public void OnSelfLeaveRoom(ulong userUID, ulong roomID)
        {
            if ((_transportRoomInfo == null) || (roomID != _transportRoomInfo.RoomID))
            {
                PicoTransportLog(LogLevel.Info, $"room leave response, not in this room({roomID}) now, skip this response");
                return;
            }
            if (userUID == _serviceInfo.SelfUID)
            {
                CurrentState = EPicoServiceState.Stopped;
                TryIssueTransoprtEvent(NetworkEvent.Disconnect, _serviceInfo.SelfUID);
                StopPicoTransport();
            }
        }

        public void OnRoomInfoUpdate(TransportRoomInfo transportRoomInfo)
        {
            PicoTransportLog(LogLevel.Info, $"OnRoomInfoUpdate 1, got roomInfo, player num: {transportRoomInfo.RoomUIDs.Count}");
            if (null == transportRoomInfo)
            {
                PicoTransportLog(LogLevel.Error, "OnRoomInfoUpdate, roomInfo is null");
                return;
            }
            if (transportRoomInfo.RoomID != _transportRoomInfo.RoomID)
            {
                PicoTransportLog(LogLevel.Error, $"got non-current room info, in roomID:{transportRoomInfo.RoomID} vs current roomID: {_transportRoomInfo.RoomID}");
                return;
            }

            if (CheckNetcodeStartStopEvent(transportRoomInfo.OwnerUID, transportRoomInfo.RoomUIDs))
            {
                return;
            }
            HashSet<ulong> newRoomUIDs = transportRoomInfo.RoomUIDs;
            HashSet<ulong> oldRoomUIDs = _serviceInfo.CurRoomUIDs;
            ulong oldOwnerUID = _serviceInfo.CurOwnerID;
            _serviceInfo.CurOwnerID = transportRoomInfo.OwnerUID;
            if ((oldOwnerUID != _serviceInfo.CurOwnerID) && (oldOwnerUID != 0))
            {
                PicoTransportLog(LogLevel.Error, $"111, owner changed from {oldOwnerUID} to: {_serviceInfo.CurOwnerID}");
                if (NetworkManager.Singleton != null)
                {
                    //is migrate condition: 1) has new room owner; 2). self is not old room owner; 3). self is still in room;
                    _serviceInfo.IsHostMigrate = _serviceInfo.AllowHostMigrate && (transportRoomInfo.OwnerUID != 0) && (_serviceInfo.SelfUID != oldOwnerUID) && (newRoomUIDs.Contains(_serviceInfo.SelfUID));
                    PicoTransportLog(LogLevel.Error, $"owner changed, stop transport now!, IsHostMigrate {_serviceInfo.IsHostMigrate}, selfUID {_serviceInfo.SelfUID}, oldOwnerUID {oldOwnerUID}");
                    StopPicoTransport();
                    return;
                }
            }
            if (_transportState == ETransportEvent.Started)
            {
                //find out those left && new entered
                foreach (ulong olduid in oldRoomUIDs)
                {
                    PicoTransportLog(LogLevel.Info, "check event of olduid:{olduid}");
                    if (!newRoomUIDs.Contains(olduid))
                    {
                        //left player
                        PicoTransportLog(LogLevel.Info, $"try notify client disconnect event, uid in event:{olduid}, ownerUID:{_transportRoomInfo.OwnerUID}");
                        TryIssueTransoprtEvent(NetworkEvent.Disconnect, olduid);
                    }
                }
                foreach (ulong newuid in newRoomUIDs)
                {
                    PicoTransportLog(LogLevel.Info, $"check event of newuid:{newuid}");
                    if (!oldRoomUIDs.Contains(newuid))
                    {
                        //new entered player
                        PicoTransportLog(LogLevel.Info, $"try notify client connect event, uid in event:{newuid}, ownerUID:{_transportRoomInfo.OwnerUID}");
                        TryIssueTransoprtEvent(NetworkEvent.Connect, newuid);
                    }
                }
            }
            _serviceInfo.CurRoomUIDs = new HashSet<ulong>(newRoomUIDs);
        }

        public void OnPkgRecved(ulong senderUID, ArraySegment<byte> pkg)
        {
            InvokePicoTransportEvent(NetworkEvent.Data, senderUID, pkg);
        }
    }  //PicoTransport
}//Transport.Pico
