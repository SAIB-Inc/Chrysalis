using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>The time limits for replies the peer owes on one connection. A wait that passes its limit ends the connection.</summary>
internal sealed class ReplyLimit(AgentChannel channel)
{
    /// <summary>The handshake reply's limit, on both clients.</summary>
    internal static readonly TimeSpan Handshake = TimeSpan.FromSeconds(10);
    /// <summary>The keep-alive reply's limit on a node-to-node connection: the node's own figure.</summary>
    internal static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(60);
    // The longest due time a timer accepts.
    private static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
    // Kept as ticks, so that a set and a read on two threads never see half a value.
    private long _ticks = TimeSpan.FromSeconds(180).Ticks;

    /// <summary>The clock that runs the timers. Tests replace it.</summary>
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>The limit for node-to-client replies. <see cref="Timeout.InfiniteTimeSpan"/> turns it off.</summary>
    internal TimeSpan Value
    {
        get => new(Volatile.Read(ref _ticks));
        set
        {
            if (value != Timeout.InfiniteTimeSpan)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Longest);
            }
            Volatile.Write(ref _ticks, value.Ticks);
        }
    }

    /// <summary>Starts <see cref="Value"/> for one reply, or returns null when it is off. Dispose the result when the call leaves.</summary>
    internal Wait? Start(ProtocolType protocol)
    {
        // Read once: the value tested and the value handed to the timer are the same.
        TimeSpan limit = Value;
        return limit == Timeout.InfiniteTimeSpan ? null : Start(protocol, limit);
    }

    /// <summary>Starts the given limit for one reply.</summary>
    internal Wait Start(ProtocolType protocol, TimeSpan limit) => new(channel, protocol, limit, Clock);

    internal sealed class Wait : IDisposable
    {
        private readonly AgentChannel _channel;
        private readonly ProtocolType _protocol;
        private readonly TimeSpan _limit;
        private readonly ITimer _timer;
        private int _settled;

        internal Wait(AgentChannel channel, ProtocolType protocol, TimeSpan limit, TimeProvider clock)
        {
            _channel = channel;
            _protocol = protocol;
            _limit = limit;
            _timer = clock.CreateTimer(static state => ((Wait)state!).Expire(), this, limit, Timeout.InfiniteTimeSpan);
        }

        // Expiry and disposal each settle the wait once; a timer callback that runs after the reply arrived does nothing.
        private void Expire()
        {
            if (Interlocked.Exchange(ref _settled, 1) == 0)
            {
                _channel.End(new ReplyTimeoutException(_protocol, _limit));
            }
        }

        public void Dispose()
        {
            _ = Interlocked.Exchange(ref _settled, 1);
            _timer.Dispose();
        }
    }
}
