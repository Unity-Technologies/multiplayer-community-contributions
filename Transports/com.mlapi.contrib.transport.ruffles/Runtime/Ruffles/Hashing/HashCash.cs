namespace Ruffles.Hashing
{
    internal static class HashCash
    {
        internal static bool TrySolve(ulong challenge, byte difficulty, ulong maxIterations, out ulong additionsRequired)
        {
            additionsRequired = 0;
            ulong iterations = 0;
            ulong workingValue = HashProvider.GetStableHash64(challenge + additionsRequired);

            while (difficulty > 0 && ((workingValue << ((sizeof(ulong) * 8) - difficulty)) >> ((sizeof(ulong) * 8) - difficulty)) != 0 && iterations < maxIterations)
            {
                ++iterations;
                ++additionsRequired;
                workingValue = HashProvider.GetStableHash64(challenge + additionsRequired);
            }

            return Validate(challenge, additionsRequired, difficulty);
        }

        internal static bool Validate(ulong challenge, ulong additionsRequired, byte difficulty)
        {
            return (difficulty == 0 || ((HashProvider.GetStableHash64(challenge + additionsRequired) << ((sizeof(ulong) * 8) - difficulty)) >> ((sizeof(ulong) * 8) - difficulty)) == 0);
        }
    }
}
