using System;
using Ruffles.Configuration;
using Ruffles.Connections;
using Ruffles.Memory;

namespace Ruffles.Channeling
{
    internal interface IChannel
    {
        HeapPointers HandleIncomingMessagePoll(ArraySegment<byte> payload);
        void CreateOutgoingMessage(ArraySegment<byte> payload, bool noMerge, ulong notificationKey);
        void HandleAck(ArraySegment<byte> payload);
        void Release();
        void Assign(byte channelId, Connection connection, SocketConfig config, MemoryManager memoryManager);
        void InternalUpdate(out bool timeout);
    }
}
