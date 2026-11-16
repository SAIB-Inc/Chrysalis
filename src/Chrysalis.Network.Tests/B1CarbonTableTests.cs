using System.Buffers;
using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>CDDL definite, strictly ascending, distinct version-table contracts on every entry point.</summary>
public sealed class B1CarbonTableTests
{
    /// <summary>Both handshake entry points normalise either wire family, including an indefinite-marked source.</summary>
    /// <param name="n2c">Whether the proposal uses the N2C wire family.</param>
    /// <param name="generic">Whether the generic send API is used.</param>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task HandshakeWritesDefiniteAscendingTables(bool n2c, bool generic)
    {
        VersionTable table = DescendingTable(n2c);
        if (table is N2CVersionTable clientTable)
        {
            IndefiniteStateTracker.SetIndefinite(clientTable.Value);
        }
        if (table is N2NVersionTable nodeTable)
        {
            IndefiniteStateTracker.SetIndefinite(nodeTable.Value);
        }
        using ProtocolReplay replay = new(ProtocolType.Handshake, Convert.FromHexString("8203a0"));
        Handshake handshake = new(replay.Channel);
        ProposeVersions proposal = HandshakeMessages.ProposeVersions(table);
        if (generic)
        {
            _ = await handshake.SendAsync<HandshakeMessage>(proposal, CancellationToken.None);
        }
        else
        {
            _ = await handshake.SendAsync(proposal, CancellationToken.None);
        }
        string expected = n2c ? "8200a219801082182af519801782182af5" : "8200a20b84182af500f40e84182af500f4";
        Assert.Equal(Convert.FromHexString(expected), await replay.ReadSentPayloadAsync(CancellationToken.None));
    }

    /// <summary>The symmetric codec hook rejects a hand-built unsorted table instead of emitting invalid wire bytes.</summary>
    /// <param name="n2c">Whether to check the N2C family.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DirectSerializerRejectsUnsortedTables(bool n2c) =>
        _ = Assert.ThrowsAny<Exception>(() => CborSerializer.Serialize(DescendingTable(n2c)));

    /// <summary>Duplicate numeric versions remain invalid even with a caller-supplied reference comparer.</summary>
    /// <param name="n2c">Whether to check the N2C family.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DirectWriterRejectsDuplicateWireVersionsAndSerializerPreservesCause(bool n2c)
    {
        VersionTable table = DuplicateTable(n2c);
        ArrayBufferWriter<byte> output = new();
        if (table is N2CVersionTable clientTable)
        {
            _ = Assert.Throws<ArgumentException>(() => N2CVersionTable.Write(output, clientTable));
            InvalidOperationException wrapped = Assert.Throws<InvalidOperationException>(() => CborSerializer.Serialize(clientTable));
            _ = Assert.IsType<ArgumentException>(wrapped.InnerException);
        }
        else
        {
            N2NVersionTable nodeTable = Assert.IsType<N2NVersionTable>(table);
            _ = Assert.Throws<ArgumentException>(() => N2NVersionTable.Write(output, nodeTable));
            InvalidOperationException wrapped = Assert.Throws<InvalidOperationException>(() => CborSerializer.Serialize(nodeTable));
            _ = Assert.IsType<ArgumentException>(wrapped.InnerException);
        }
        Assert.Equal(0, output.WrittenCount);
    }

    /// <summary>Both send APIs reject duplicate wire values before any bytes can be written.</summary>
    /// <param name="n2c">Whether to check the N2C family.</param>
    /// <param name="generic">Whether to use the generic send API.</param>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task HandshakeRejectsDuplicateVersionsBeforeWriting(bool n2c, bool generic)
    {
        using ProtocolReplay replay = new(ProtocolType.Handshake, Convert.FromHexString("8203a0"));
        Handshake handshake = new(replay.Channel);
        ProposeVersions proposal = HandshakeMessages.ProposeVersions(DuplicateTable(n2c));
        _ = await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            if (generic)
            {
                _ = await handshake.SendAsync<HandshakeMessage>(proposal, CancellationToken.None);
            }
            else
            {
                _ = await handshake.SendAsync(proposal, CancellationToken.None);
            }
        });
        replay.AssertNothingSent();
    }

    /// <summary>The reviewer's independent wire vectors and N2N equivalents must fail on decode.</summary>
    /// <param name="hex">An indefinite, descending or duplicate-key query reply.</param>
    [Theory]
    [InlineData("8203bf19801782182af4ff")]
    [InlineData("8203a219801782182af419801082182af4")]
    [InlineData("8203a21980178201f41980178202f4")]
    [InlineData("8203bf0e84182af500f4ff")]
    [InlineData("8203a20e84182af500f40b84182af500f4")]
    [InlineData("8203a20e8401f500f40e8402f500f4")]
    public void DecoderRejectsInvalidTables(string hex) =>
        _ = Assert.ThrowsAny<Exception>(() => CborSerializer.Deserialize<HandshakeMessage>(Convert.FromHexString(hex)));

    /// <summary>The existing factories remain directly serializable and round-trip without changing their versions.</summary>
    [Fact]
    public void SortedFactoryTablesRemainSerializable()
    {
        N2CVersionTable n2c = VersionTables.N2cV10AndAbove(42);
        N2NVersionTable n2n = VersionTables.N2nV11AndAbove(42);
        Assert.Equal(n2c.Value.Keys, CborSerializer.Deserialize<N2CVersionTable>(CborSerializer.Serialize(n2c)).Value.Keys);
        Assert.Equal(n2n.Value.Keys, CborSerializer.Deserialize<N2NVersionTable>(CborSerializer.Serialize(n2n)).Value.Keys);
    }

    /// <summary>Both send APIs reject the reviewer's duplicate-key reply, not just a hand-built offer.</summary>
    /// <param name="generic">Whether to use the generic send API.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandshakeRejectsDuplicateKeyReply(bool generic)
    {
        using ProtocolReplay replay = new(ProtocolType.Handshake, Convert.FromHexString("8203a21980178201f41980178202f4"));
        Handshake handshake = new(replay.Channel);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(1));
        Exception failure = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            if (generic)
            {
                _ = await handshake.SendAsync<HandshakeMessage>(HandshakeMessages.ProposeVersions(VersionTables.N2cV10AndAbove()), deadline.Token);
            }
            else
            {
                _ = await handshake.SendAsync(HandshakeMessages.ProposeVersions(VersionTables.N2cV10AndAbove()), deadline.Token);
            }
        });
        Assert.IsNotType<HandshakeRefusedException>(failure);
        Assert.False(failure is OperationCanceledException);
        Assert.False(handshake.IsDone);
    }

    /// <summary>Recorded limitation, not a desired contract: the generated concrete map reader drops duplicate keys before validation.</summary>
    /// <param name="n2c">Whether to record the N2C or N2N concrete-table decoder.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecordedGeneratorLimitation_DirectConcreteTableDecodeSilentlyDropsDuplicateKeys(bool n2c)
    {
        if (n2c)
        {
            N2CVersionTable table = CborSerializer.Deserialize<N2CVersionTable>(Convert.FromHexString("a21980178201f41980178202f4"));
            Assert.Equal(1UL, Assert.Single(table.Value).Value.NetworkMagic);
            Assert.Equal(Convert.FromHexString("a21980178201f41980178202f4"), CborSerializer.Serialize(table));
            N2CVersionTable created = N2CVersionTable.Create(Assert.IsType<N2CVersionTable>(DuplicateTable(true)).Value);
            _ = Assert.Single(created.Value);
            Assert.Equal(2, created.Raw.Span[0] & 31);
        }
        else
        {
            N2NVersionTable table = CborSerializer.Deserialize<N2NVersionTable>(Convert.FromHexString("a20e8401f500f40e8402f500f4"));
            Assert.Equal(1UL, Assert.Single(table.Value).Value.NetworkMagic);
            Assert.Equal(Convert.FromHexString("a20e8401f500f40e8402f500f4"), CborSerializer.Serialize(table));
            N2NVersionTable created = N2NVersionTable.Create(Assert.IsType<N2NVersionTable>(DuplicateTable(false)).Value);
            _ = Assert.Single(created.Value);
            Assert.Equal(2, created.Raw.Span[0] & 31);
        }
    }

    private static VersionTable DescendingTable(bool n2c) => n2c
        ? new N2CVersionTable(new Dictionary<N2CVersion, N2CVersionData> { [N2CVersions.V23] = new(42, true), [N2CVersions.V16] = new(42, true) })
        : new N2NVersionTable(new Dictionary<N2NVersion, N2NVersionData> { [N2NVersions.V14] = new(42, true, 0, false), [N2NVersions.V11] = new(42, true, 0, false) });

    private static VersionTable DuplicateTable(bool n2c) => n2c
        ? new N2CVersionTable(new Dictionary<N2CVersion, N2CVersionData>(ReferenceEqualityComparer.Instance) { [new(32791)] = new(42, false), [new(32791)] = new(42, false) })
        : new N2NVersionTable(new Dictionary<N2NVersion, N2NVersionData>(ReferenceEqualityComparer.Instance) { [new(14)] = new(42, true, 0, false), [new(14)] = new(42, true, 0, false) });
}
