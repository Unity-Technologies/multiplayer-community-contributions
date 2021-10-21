namespace Ruffles.Random
{
    internal static class RandomProvider
    {
        private static readonly System.Random _random = new System.Random();
        private static readonly byte[] _randomBuffer = new byte[8];
        private static readonly object _randomLock = new object();

        internal static ulong GetRandomULong()
        {
            lock (_randomLock)
            {
                _random.NextBytes(_randomBuffer);

                return (((ulong)_randomBuffer[0]) |
                        ((ulong)_randomBuffer[1] << 8) |
                        ((ulong)_randomBuffer[2] << 16) |
                        ((ulong)_randomBuffer[3] << 24) |
                        ((ulong)_randomBuffer[4] << 32) |
                        ((ulong)_randomBuffer[5] << 40) |
                        ((ulong)_randomBuffer[6] << 48) |
                        ((ulong)_randomBuffer[7] << 56));
            }
        }
    }
}
