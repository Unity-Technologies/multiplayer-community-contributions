namespace Ruffles.Hashing
{
    internal static class HashProvider
    {
        private const uint FNV_offset_basis32 = 2166136261;
        private const uint FNV_prime32 = 16777619;

        private const ulong FNV_offset_basis64 = 14695981039346656037;
        private const ulong FNV_prime64 = 1099511628211;

        internal static ulong GetStableHash64(byte[] bytes)
        {
            unchecked
            {
                ulong hash = FNV_offset_basis64;
                for (int i = 0; i < bytes.Length; i++)
                {
                    ulong bt = bytes[i];
                    hash = hash * FNV_prime64;
                    hash = hash ^ bt;
                }

                return hash;
            }
        }

        internal static ulong GetStableHash64(ulong value)
        {
            unchecked
            {
                ulong hash = FNV_offset_basis64;

                for (byte i = 0; i < sizeof(ulong); i++)
                {
                    ulong bt = ((byte)(value >> (i * 8)));
                    hash = hash * FNV_prime64;
                    hash = hash ^ bt;
                }

                return hash;
            }
        }

        internal static ulong GetStableHash64(ulong value1, ulong value2, ulong value3)
        {
            unchecked
            {
                ulong hash = FNV_offset_basis64;

                for (byte i = 0; i < sizeof(ulong); i++)
                {
                    ulong bt = ((byte)(value1 >> (i * 8)));
                    hash = hash * FNV_prime64;
                    hash = hash ^ bt;
                }

                for (byte i = 0; i < sizeof(ulong); i++)
                {
                    ulong bt = ((byte)(value2 >> (i * 8)));
                    hash = hash * FNV_prime64;
                    hash = hash ^ bt;
                }

                for (byte i = 0; i < sizeof(ulong); i++)
                {
                    ulong bt = ((byte)(value3 >> (i * 8)));
                    hash = hash * FNV_prime64;
                    hash = hash ^ bt;
                }

                return hash;
            }
        }
    }
}
