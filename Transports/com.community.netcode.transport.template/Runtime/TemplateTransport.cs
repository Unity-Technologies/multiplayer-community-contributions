using System;
using Unity.Netcode;

namespace Netcode.Transports.Template
{
    public class TemplateTransport : NetworkTransport
    {
        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkDelivery delivery)
        {
            throw new NotImplementedException();
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            throw new NotImplementedException();
        }

        public override bool StartClient()
        {
            throw new NotImplementedException();
        }

        public override bool StartServer()
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

        public override void Initialize()
        {
            throw new NotImplementedException();
        }

        public override ulong ServerClientId { get; }
    }
}
