using System;
using MLAPI.Transports.Tasks;

namespace MLAPI.Transports.Template
{
    public class TemplateTransport : NetworkTransport
    {
        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkChannel channel)
        {
            throw new NotImplementedException();
        }

        public override NetworkEvent PollEvent(out ulong clientId, out NetworkChannel channel, out ArraySegment<byte> payload, out float receiveTime)
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
