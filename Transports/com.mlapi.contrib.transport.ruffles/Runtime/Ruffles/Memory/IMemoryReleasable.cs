namespace Ruffles.Memory
{
    internal interface IMemoryReleasable
    {
        bool IsAlloced { get; }
        void DeAlloc(MemoryManager memoryManager);
    }
}
