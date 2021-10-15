namespace Ruffles.Simulation
{
    /// <summary>
    /// Struct for configuring the network simulator.
    /// </summary>
    public struct SimulatorConfig
    {
        /// <summary>
        /// The percentage of packets that will be dropped. Value between 0-1.
        /// </summary>
        public float DropPercentage;
        /// <summary>
        /// The minimum amount of random latency every packet will get in milliseconds.
        /// </summary>
        public int MinLatency;
        /// <summary>
        /// The maximum amount of random latency every packet will get in milliseconds.
        /// </summary>
        public int MaxLatency;
    }
}
