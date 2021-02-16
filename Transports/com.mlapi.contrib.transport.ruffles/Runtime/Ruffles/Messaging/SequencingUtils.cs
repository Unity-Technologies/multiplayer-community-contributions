namespace Ruffles.Messaging
{
    internal static class SequencingUtils
    {
        internal static long Distance(ulong from, ulong to, byte bytes)
        {
            int _shift = (sizeof(ulong) - bytes) * 8;

            to <<= _shift;
            from <<= _shift;

            return ((long)(from - to)) >> _shift;
        }
    }
}
