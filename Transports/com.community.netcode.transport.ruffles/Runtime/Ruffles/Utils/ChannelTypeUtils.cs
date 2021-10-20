using Ruffles.Channeling;

namespace Ruffles.Utils
{
    internal static class ChannelTypeUtils
    {
        internal static bool IsValidChannelType(byte channelType)
        {
            return channelType == (byte)ChannelType.Reliable ||
                   channelType == (byte)ChannelType.Unreliable ||
                   channelType == (byte)ChannelType.UnreliableOrdered ||
                   channelType == (byte)ChannelType.ReliableSequenced ||
                   channelType == (byte)ChannelType.UnreliableRaw ||
                   channelType == (byte)ChannelType.ReliableSequencedFragmented ||
                   channelType == (byte)ChannelType.ReliableOrdered ||
                   channelType == (byte)ChannelType.ReliableFragmented;
        }

        internal static bool IsValidChannelType(ChannelType channelType)
        {
            return channelType == ChannelType.Reliable ||
                   channelType == ChannelType.Unreliable ||
                   channelType == ChannelType.UnreliableOrdered ||
                   channelType == ChannelType.ReliableSequenced ||
                   channelType == ChannelType.UnreliableRaw ||
                   channelType == ChannelType.ReliableSequencedFragmented ||
                   channelType == ChannelType.ReliableOrdered ||
                   channelType == ChannelType.ReliableFragmented;
        }

        internal static byte ToByte(ChannelType channelType)
        {
            return (byte)channelType;
        }

        internal static ChannelType FromByte(byte channelType)
        {
            return (ChannelType)channelType;
        }
    }
}
