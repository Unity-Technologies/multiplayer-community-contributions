using Ruffles.Channeling.Channels;
using Ruffles.Collections;
using Ruffles.Configuration;
using Ruffles.Connections;
using Ruffles.Memory;

namespace Ruffles.Channeling
{
    internal class ChannelPool
    {
        private readonly ConcurrentCircularQueue<ReliableChannel> _reliableChannels;
        private readonly ConcurrentCircularQueue<ReliableFragmentedChannel> _reliableFragmentedChannels;
        private readonly ConcurrentCircularQueue<ReliableOrderedChannel> _reliableOrderedChannels;
        private readonly ConcurrentCircularQueue<ReliableSequencedChannel> _reliableSequnecedChannels;
        private readonly ConcurrentCircularQueue<ReliableSequencedFragmentedChannel> _reliableSequencedFragmentedChannels;
        private readonly ConcurrentCircularQueue<UnreliableChannel> _unreliableChannels;
        private readonly ConcurrentCircularQueue<UnreliableOrderedChannel> _unreliableOrderedChannels;
        private readonly ConcurrentCircularQueue<UnreliableRawChannel> _unreliableRawChannels;

        internal ChannelPool(SocketConfig config)
        {
            if (config.ReuseChannels)
            {
                if ((config.PooledChannels & PooledChannelType.Reliable) == PooledChannelType.Reliable)
                {
                    _reliableChannels = new ConcurrentCircularQueue<ReliableChannel>(config.ChannelPoolSize);
                }

                if ((config.PooledChannels & PooledChannelType.ReliableFragmented) == PooledChannelType.ReliableFragmented)
                {
                    _reliableFragmentedChannels = new ConcurrentCircularQueue<ReliableFragmentedChannel>(config.ChannelPoolSize);
                }

                if ((config.PooledChannels & PooledChannelType.ReliableOrdered) == PooledChannelType.ReliableOrdered)
                {
                    _reliableOrderedChannels = new ConcurrentCircularQueue<ReliableOrderedChannel>(config.ChannelPoolSize);
                }

                if ((config.PooledChannels & PooledChannelType.ReliableSequenced) == PooledChannelType.ReliableSequenced)
                {
                    _reliableSequnecedChannels = new ConcurrentCircularQueue<ReliableSequencedChannel>(config.ChannelPoolSize);
                }

                if ((config.PooledChannels & PooledChannelType.ReliableSequencedFragmented) == PooledChannelType.ReliableSequencedFragmented)
                {
                    _reliableSequencedFragmentedChannels = new ConcurrentCircularQueue<ReliableSequencedFragmentedChannel>(config.ChannelPoolSize);
                }

                if ((config.PooledChannels & PooledChannelType.Unreliable) == PooledChannelType.Unreliable)
                {
                    _unreliableChannels = new ConcurrentCircularQueue<UnreliableChannel>(config.ChannelPoolSize);
                }

                if ((config.PooledChannels & PooledChannelType.UnreliableOrdered) == PooledChannelType.UnreliableOrdered)
                {
                    _unreliableOrderedChannels = new ConcurrentCircularQueue<UnreliableOrderedChannel>(config.ChannelPoolSize);
                }

                if ((config.PooledChannels & PooledChannelType.UnreliableRaw) == PooledChannelType.UnreliableRaw)
                {
                    _unreliableRawChannels = new ConcurrentCircularQueue<UnreliableRawChannel>(config.ChannelPoolSize);
                }
            }
        }

        internal void Return(IChannel channel)
        {
            channel.Release();

            if (_reliableChannels != null && channel is ReliableChannel reliableChannel)
            {
                _reliableChannels.TryEnqueue(reliableChannel);
            }

            if (_reliableFragmentedChannels != null && channel is ReliableFragmentedChannel reliableFragmentedChannel)
            {
                _reliableFragmentedChannels.TryEnqueue(reliableFragmentedChannel);
            }

            if (_reliableOrderedChannels != null && channel is ReliableOrderedChannel reliableOrderedChannel)
            {
                _reliableOrderedChannels.TryEnqueue(reliableOrderedChannel);
            }

            if (_reliableSequnecedChannels != null && channel is ReliableSequencedChannel reliableSequencedChannel)
            {
                _reliableSequnecedChannels.TryEnqueue(reliableSequencedChannel);
            }

            if (_reliableSequencedFragmentedChannels != null && channel is ReliableSequencedFragmentedChannel reliableSequencedFragmentedChannel)
            {
                _reliableSequencedFragmentedChannels.TryEnqueue(reliableSequencedFragmentedChannel);
            }

            if (_unreliableChannels != null && channel is UnreliableChannel unreliableChannel)
            {
                _unreliableChannels.TryEnqueue(unreliableChannel);
            }

            if (_unreliableOrderedChannels != null && channel is UnreliableOrderedChannel unreliableOrderedChannel)
            {
                _unreliableOrderedChannels.TryEnqueue(unreliableOrderedChannel);
            }

            if (_unreliableRawChannels != null && channel is UnreliableRawChannel unreliableRawChannel)
            {
                _unreliableRawChannels.TryEnqueue(unreliableRawChannel);
            }
        }

        internal IChannel GetChannel(ChannelType type, byte channelId, Connection connection, SocketConfig config, MemoryManager memoryManager)
        {
            if (type == ChannelType.Reliable)
            {
                if (_reliableChannels != null && _reliableChannels.TryDequeue(out ReliableChannel channel))
                {
                    channel.Assign(channelId, connection, config, memoryManager);

                    return channel;
                }

                return new ReliableChannel(channelId, connection, config, memoryManager);
            }

            if (type == ChannelType.ReliableFragmented)
            {
                if (_reliableFragmentedChannels != null && _reliableFragmentedChannels.TryDequeue(out ReliableFragmentedChannel channel))
                {
                    channel.Assign(channelId, connection, config, memoryManager);

                    return channel;
                }

                return new ReliableFragmentedChannel(channelId, connection, config, memoryManager);
            }

            if (type == ChannelType.ReliableOrdered)
            {
                if (_reliableOrderedChannels != null && _reliableOrderedChannels.TryDequeue(out ReliableOrderedChannel channel))
                {
                    channel.Assign(channelId, connection, config, memoryManager);

                    return channel;
                }

                return new ReliableOrderedChannel(channelId, connection, config, memoryManager);
            }

            if (type == ChannelType.ReliableSequenced)
            {
                if (_reliableSequnecedChannels != null && _reliableSequnecedChannels.TryDequeue(out ReliableSequencedChannel channel))
                {
                    channel.Assign(channelId, connection, config, memoryManager);

                    return channel;
                }

                return new ReliableSequencedChannel(channelId, connection, config, memoryManager);
            }

            if (type == ChannelType.ReliableSequencedFragmented)
            {
                if (_reliableSequencedFragmentedChannels != null && _reliableSequencedFragmentedChannels.TryDequeue(out ReliableSequencedFragmentedChannel channel))
                {
                    channel.Assign(channelId, connection, config, memoryManager);

                    return channel;
                }

                return new ReliableSequencedFragmentedChannel(channelId, connection, config, memoryManager);
            }

            if (type == ChannelType.Unreliable)
            {
                if (_unreliableChannels != null && _unreliableChannels.TryDequeue(out UnreliableChannel channel))
                {
                    channel.Assign(channelId, connection, config, memoryManager);

                    return channel;
                }

                return new UnreliableChannel(channelId, connection, config, memoryManager);
            }

            if (type == ChannelType.UnreliableOrdered)
            {
                if (_unreliableOrderedChannels != null && _unreliableOrderedChannels.TryDequeue(out UnreliableOrderedChannel channel))
                {
                    channel.Assign(channelId, connection, config, memoryManager);

                    return channel;
                }

                return new UnreliableOrderedChannel(channelId, connection, config, memoryManager);
            }

            if (type == ChannelType.UnreliableRaw)
            {
                if (_unreliableRawChannels != null && _unreliableRawChannels.TryDequeue(out UnreliableRawChannel channel))
                {
                    channel.Assign(channelId, connection, config, memoryManager);

                    return channel;
                }

                return new UnreliableRawChannel(channelId, connection, config, memoryManager);
            }

            return null;
        }
    }
}
