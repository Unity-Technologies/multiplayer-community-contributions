using System;
using System.Collections.Generic;
using System.Net;
using Ruffles.Connections;
using Ruffles.Time;

namespace Ruffles.Simulation
{
    internal class NetworkSimulator
    {
        internal struct OutgoingPacket
        {
            public byte[] Data;
            public Connection Connection;
        }

        internal delegate bool SendDelegate(IPEndPoint endpoint, ArraySegment<byte> payload);

        private readonly System.Random random = new System.Random();
        private readonly object _lock = new object();
        private readonly SortedList<NetTime, OutgoingPacket> _packets = new SortedList<NetTime, OutgoingPacket>();
        private readonly SimulatorConfig config;
        private readonly SendDelegate sendDelegate;

        internal NetworkSimulator(SimulatorConfig config, SendDelegate sendDelegate)
        {
            this.config = config;
            this.sendDelegate = sendDelegate;
        }

        internal void Add(Connection connection, ArraySegment<byte> payload)
        {
            if (random.NextDouble() < (double)config.DropPercentage)
            {
                // Packet drop
                return;
            }

            byte[] garbageAlloc = new byte[payload.Count];
            Buffer.BlockCopy(payload.Array, payload.Offset, garbageAlloc, 0, payload.Count);

            lock (_lock)
            {
                NetTime scheduledTime;
                do
                {
                    scheduledTime = NetTime.Now.AddMilliseconds(random.Next(config.MinLatency, config.MaxLatency));
                }
                while (_packets.ContainsKey(scheduledTime));

                _packets.Add(scheduledTime, new OutgoingPacket()
                {
                    Data = garbageAlloc,
                    Connection = connection
                });
            }
        }

        internal void RunLoop()
        {
            lock (_lock)
            {
                while (_packets.Keys.Count > 0 && NetTime.Now >= _packets.Keys[0])
                {
                    sendDelegate(_packets[_packets.Keys[0]].Connection.EndPoint, new ArraySegment<byte>(_packets[_packets.Keys[0]].Data, 0, _packets[_packets.Keys[0]].Data.Length));
                    _packets.RemoveAt(0);
                }
            }
        }

        internal void Flush()
        {
            lock (_lock)
            {
                while (_packets.Keys.Count > 0)
                {
                    sendDelegate(_packets[_packets.Keys[0]].Connection.EndPoint, new ArraySegment<byte>(_packets[_packets.Keys[0]].Data, 0, _packets[_packets.Keys[0]].Data.Length));
                    _packets.RemoveAt(0);
                }
            }
        }
    }
}
