using System.Net;

namespace Ruffles.Configuration
{
    internal static class Constants
    {
        // 7x NULL | Ruffles Greets You | 7x NULL
        internal static readonly byte[] RUFFLES_PROTOCOL_IDENTIFICATION = new byte[32] { 00, 00, 00, 00, 00, 00, 00, 82, 117, 102, 102, 108, 101, 115, 32, 71, 114, 101, 101, 116, 115, 32, 89, 111, 117, 00, 00, 00, 00, 00, 00, 00 };
        internal static readonly int RECEIVE_SOCKET_BUFFER_SIZE = 1024 * 1024;
        internal static readonly int SEND_SOCKET_BUFFER_SIZE = 1024 * 1024;
        internal static readonly int SOCKET_PACKET_TTL = 64;
        internal static readonly int MAX_CHANNELS = byte.MaxValue;
        internal static readonly int MAX_FRAGMENTS = 32768;
        internal static readonly IPAddress IPv6AllDevicesMulticastAddress = IPAddress.Parse("FF02:0:0:0:0:0:0:1");
        internal static readonly int MINIMUM_MTU = 512;
    }
}
