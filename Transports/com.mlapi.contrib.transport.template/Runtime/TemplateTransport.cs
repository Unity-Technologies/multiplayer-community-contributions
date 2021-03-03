using System;
using MLAPI.Transports;
using MLAPI.Transports.Tasks;

namespace TemplateTransport
{
    public class TemplateTransport : Transport
    {
        public override void Send(ulong clientId, ArraySegment<byte> data, string channelName)
        {
            throw new NotImplementedException();
        }

        public override NetEventType PollEvent(out ulong clientId, out string channelName, out ArraySegment<byte> payload, out float receiveTime)
        {
            throw new NotImplementedException();
        }

        public override SocketTasks StartClient()
        {
            throw new NotImplementedException();
        }

        public override SocketTasks StartServer()
        {
            throw new NotImplementedException();
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            throw new NotImplementedException();
        }

        public override void DisconnectLocalClient()
        {
            throw new NotImplementedException();
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            throw new NotImplementedException();
        }

        public override void Shutdown()
        {
            throw new NotImplementedException();
        }

        public override void Init()
        {
            throw new NotImplementedException();
        }

        public override ulong ServerClientId { get; }
    }
}
