using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Reflection;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>NodeClient startup failure contracts exercised entirely over in-memory bearer pipes.</summary>
public sealed class B1CarbonNodeClientTests
{
    /// <summary>An unoffered version, mismatched magic or query-mode accept never publishes negotiated state.</summary>
    /// <param name="reply">A complete but incompatible accept according to the pinned CDDL.</param>
    [Theory]
    [InlineData("830119801782182af4")]
    [InlineData("830119801482182bf4")]
    [InlineData("830119801482182af5")]
    public async Task InvalidAcceptLeavesNegotiatedVersionNull(string reply)
    {
        await using NodeReplay replay = new();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        Task startup = replay.Client.StartAsync(SingleOffer(), deadline.Token);
        _ = await replay.ReadProposalAsync(deadline.Token);
        await replay.ReplyAsync(reply, deadline.Token);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => startup);
        Assert.Null(replay.Client.NegotiatedVersion);
        replay.AssertNothingSent();
    }

    /// <summary>A decoded node refusal is typed and preserves its reason without publishing a version.</summary>
    [Fact]
    public async Task RefusalLeavesNegotiatedVersionNullAndRetainsTypedReason()
    {
        await using NodeReplay replay = new();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        Task startup = replay.Client.StartAsync(SingleOffer(), deadline.Token);
        _ = await replay.ReadProposalAsync(deadline.Token);
        await replay.ReplyAsync("8202830219801463626164", deadline.Token);
        HandshakeRefusedException failure = await Assert.ThrowsAsync<HandshakeRefusedException>(() => startup);
        Refused reason = Assert.IsType<Refused>(failure.Reason);
        Assert.Equal(32788, reason.VersionNumber);
        Assert.Equal("bad", reason.ErrorData);
        Assert.Null(replay.Client.NegotiatedVersion);
        replay.AssertNothingSent();
    }

    /// <summary>Startup runs once; a second attempt leaves the established version intact and sends nothing.</summary>
    [Fact]
    public async Task SecondStartIsRejectedWithoutAnotherProposal()
    {
        await using NodeReplay replay = new();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        Task startup = replay.Client.StartAsync(SingleOffer(), deadline.Token);
        _ = await replay.ReadProposalAsync(deadline.Token);
        await replay.ReplyAsync("830119801482182af4", deadline.Token);
        await startup;
        Assert.Equal(N2CVersions.V20, replay.Client.NegotiatedVersion);
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => replay.Client.StartAsync(SingleOffer(), deadline.Token));
        Assert.Equal(N2CVersions.V20, replay.Client.NegotiatedVersion);
        replay.AssertNothingSent();
    }

    /// <summary>NodeClient's explicit offer is sorted and definite before reaching the bearer.</summary>
    [Fact]
    public async Task NodeClientWritesAscendingDefiniteOffer()
    {
        await using NodeReplay replay = new();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        N2CVersionTable offer = new(new Dictionary<N2CVersion, N2CVersionData> { [N2CVersions.V23] = new(42, false), [N2CVersions.V16] = new(42, false) });
        Task startup = replay.Client.StartAsync(offer, deadline.Token);
        Assert.Equal(Convert.FromHexString("8200a219801082182af419801782182af4"), await replay.ReadProposalAsync(deadline.Token));
        await replay.ReplyAsync("830119801082182af4", deadline.Token);
        await startup;
        Assert.Equal(N2CVersions.V16, replay.Client.NegotiatedVersion);
    }

    /// <summary>All offer validation failures precede startup and any queued or unflushed wire data.</summary>
    /// <param name="invalid">The invalid offer condition to exercise.</param>
    [Theory]
    [InlineData("null-table")]
    [InlineData("null-dictionary")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("mixed-magics")]
    [InlineData("query-true")]
    [InlineData("query-null")]
    [InlineData("wrong-family")]
    [InlineData("null-first")]
    [InlineData("null-later")]
    public async Task NormalizeOfferRejectsInvalidInputWithArgumentExceptionBeforeWriting(string invalid)
    {
        await using NodeReplay replay = new();
        _ = await Assert.ThrowsAnyAsync<ArgumentException>(() => replay.Client.StartAsync(InvalidOffer(invalid)));
        Assert.Null(replay.Client.NegotiatedVersion);
        Assert.False(replay.Client.IsPlexerHealthy());
        replay.AssertNothingSent();
    }

    /// <summary>A NodeClient-owned but unstarted context rejects both version-dependent protocols before any queued byte.</summary>
    /// <param name="monitor">Whether to exercise GetMeasures instead of QueryAsync.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnstartedClientGuardsBeforeAnyQueuedOrUnflushedWrite(bool monitor)
    {
        await using NodeReplay replay = new();
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(1));
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (monitor)
            {
                _ = await replay.Client.LocalTxMonitor.GetMeasuresAsync(deadline.Token);
            }
            else
            {
                _ = await replay.Client.LocalStateQuery.QueryAsync(RawQueries.GetCurrentEra, deadline.Token);
            }
        });
        Assert.Null(replay.Client.NegotiatedVersion);
        replay.AssertNothingSent();
    }

    private static N2CVersionTable SingleOffer() => new(new Dictionary<N2CVersion, N2CVersionData> { [N2CVersions.V20] = new(42, false) });

    private static N2CVersionTable InvalidOffer(string invalid) => invalid switch
    {
        "null-table" => null!,
        "null-dictionary" => new(null!),
        "empty" => new([]),
        "duplicate" => new(new Dictionary<N2CVersion, N2CVersionData>(ReferenceEqualityComparer.Instance) { [new(32788)] = new(42, false), [new(32788)] = new(42, false) }),
        "mixed-magics" => new(new Dictionary<N2CVersion, N2CVersionData> { [N2CVersions.V20] = new(42, false), [N2CVersions.V21] = new(43, false) }),
        "query-true" => new(new Dictionary<N2CVersion, N2CVersionData> { [N2CVersions.V20] = new(42, true) }),
        "query-null" => new(new Dictionary<N2CVersion, N2CVersionData> { [N2CVersions.V20] = new(42, null) }),
        "wrong-family" => new(new Dictionary<N2CVersion, N2CVersionData> { [new(14)] = new(42, false) }),
        "null-first" => new(new Dictionary<N2CVersion, N2CVersionData> { [N2CVersions.V20] = null! }),
        "null-later" => new(new Dictionary<N2CVersion, N2CVersionData> { [N2CVersions.V20] = new(42, false), [N2CVersions.V21] = null! }),
        _ => throw new ArgumentOutOfRangeException(nameof(invalid))
    };

    private sealed class NodeReplay : IBearer, IAsyncDisposable
    {
        private readonly Pipe _incoming = new();
        private readonly Pipe _outgoing = new();
        private readonly Pipe _muxPipe;
        private readonly Plexer _plexer;
        private bool _disposed;

        internal NodeReplay()
        {
            _plexer = new Plexer(this);
            Client = (NodeClient)Activator.CreateInstance(typeof(NodeClient), BindingFlags.Instance | BindingFlags.NonPublic, null, [_plexer], null)!;
            Muxer muxer = (Muxer)typeof(Plexer).GetField("_muxer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_plexer)!;
            _muxPipe = (Pipe)typeof(Muxer).GetField("_pipe", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(muxer)!;
        }

        internal NodeClient Client { get; }
        public PipeReader Reader => _incoming.Reader;
        public PipeWriter Writer => _outgoing.Writer;

        internal async Task<byte[]> ReadProposalAsync(CancellationToken token)
        {
            byte[] header = new byte[8];
            while (true)
            {
                ReadResult read = await _outgoing.Reader.ReadAsync(token);
                if (read.Buffer.Length >= header.Length)
                {
                    read.Buffer.Slice(0, header.Length).CopyTo(header);
                    int size = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6));
                    if (read.Buffer.Length >= header.Length + size)
                    {
                        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)));
                        byte[] payload = read.Buffer.Slice(header.Length, size).ToArray();
                        _outgoing.Reader.AdvanceTo(read.Buffer.GetPosition(header.Length + size));
                        return payload;
                    }
                }
                Assert.False(read.IsCompleted, "The client stopped before sending a complete proposal.");
                _outgoing.Reader.AdvanceTo(read.Buffer.Start, read.Buffer.End);
            }
        }

        internal async Task ReplyAsync(string hex, CancellationToken token)
        {
            byte[] payload = Convert.FromHexString(hex);
            byte[] segment = new byte[8 + payload.Length];
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(4), 0x8000);
            BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(6), checked((ushort)payload.Length));
            payload.CopyTo(segment, 8);
            _ = await _incoming.Writer.WriteAsync(segment, token);
        }

        internal void AssertNothingSent()
        {
            Assert.Equal(0, _outgoing.Writer.UnflushedBytes);
            AssertEmpty(_outgoing.Reader);
            Assert.Equal(0, _muxPipe.Writer.UnflushedBytes);
            // Only inspect the internal queue when the muxer is not its active reader.
            if (typeof(NodeClient).GetField("_plexerTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client) is null)
            {
                AssertEmpty(_muxPipe.Reader);
            }
        }

        private static void AssertEmpty(PipeReader reader)
        {
            if (reader.TryRead(out ReadResult read))
            {
                bool empty = read.Buffer.IsEmpty;
                reader.AdvanceTo(read.Buffer.End);
                Assert.True(empty, "A rejected client operation queued outgoing bytes.");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _incoming.Reader.Complete();
            _outgoing.Writer.Complete();
        }

        public async ValueTask DisposeAsync()
        {
            Task? running = (Task?)typeof(NodeClient).GetField("_plexerTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Client);
            _plexer.Dispose();
            // Observe the existing Plexer's unspecified disposal exception, but never
            // allow a leaked run loop or a teardown timeout to count as a passing test.
            if (running is not null)
            {
                Exception? termination = await Record.ExceptionAsync(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.IsNotType<TimeoutException>(termination);
            }
            await _incoming.Writer.CompleteAsync();
            await _outgoing.Reader.CompleteAsync();
            await _muxPipe.Reader.CompleteAsync();
        }
    }
}
