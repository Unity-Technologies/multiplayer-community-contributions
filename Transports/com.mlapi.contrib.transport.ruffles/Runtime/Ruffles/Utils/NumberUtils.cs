namespace Ruffles.Utils
{
    internal static class NumberUtils
    {
        internal static int WrapMod(int x, int m)
        {
            int r = x % m;
            return r < 0 ? r + m : r;
        }
    }
}
