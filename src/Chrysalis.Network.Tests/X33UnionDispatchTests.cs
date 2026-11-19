using System.Runtime.ExceptionServices;
using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Xunit;

namespace Chrysalis.Network.Tests;

public sealed class X33UnionDispatchTests
{
    private delegate T Read<T>(ReadOnlyMemory<byte> data, out int consumed);

    [Fact]
    public void NodeToClientAcceptDoesNotProbeByThrowing()
    {
        byte[] bytes = PreprodFixtures.Read("wire/c4-in-p0-001.cbor");
        N2CAcceptVersion value = CheckRead<HandshakeMessage, N2CAcceptVersion>(HandshakeMessage.Read, bytes);
        Assert.Equal(32790, value.Version.Value);
        Assert.Equal(1UL, value.VersionData.NetworkMagic);
    }

    [Fact]
    public void NodeToNodeVersionTableDoesNotProbeByThrowing()
    {
        KeyValuePair<N2NVersion, N2NVersionData> version = Assert.Single(CheckRead<VersionTable, N2NVersionTable>(
            VersionTable.Read, Convert.FromHexString("A10E84182AF500F4")).Value);
        Assert.Equal(14, version.Key.Value);
        Assert.Equal(42UL, version.Value.NetworkMagic);
    }

    [Fact]
    public void AddressQueryDoesNotProbeByThrowing()
    {
        byte[] address = Convert.FromHexString("6011111111111111111111111111111111111111111111111111111111");
        UtxoByAddressQuery query = CheckRead<IQueryReq, UtxoByAddressQuery>(IQueryReq.Read,
            Convert.FromHexString("820681581D6011111111111111111111111111111111111111111111111111111111"));
        Assert.Equal(6UL, query.Idx);
        Assert.Equal(address, Assert.Single(query.Addresses).ToArray());
    }

    [Fact]
    public void TransactionInputQueryDoesNotProbeByThrowing()
    {
        UtxoByTxInQuery query = CheckRead<IQueryReq, UtxoByTxInQuery>(IQueryReq.Read,
            Convert.FromHexString("820F81825820111111111111111111111111111111111111111111111111111111111111111100"));
        Assert.Equal(15UL, query.Idx);
        Assert.Equal(Convert.FromHexString("825820111111111111111111111111111111111111111111111111111111111111111100"),
            Assert.Single(query.TxIns).Raw.ToArray());
    }

    [Fact]
    public void EncodedHeaderDoesNotProbeByThrowing() =>
        Assert.Equal(new byte[] { 0x82, 0x01, 0x02 }, CheckRead<INetworkBlockHeader, ShelleyN2NHeader>(INetworkBlockHeader.Read, Convert.FromHexString("D81843820102")).Header.Value.ToArray());

    [Theory]
    [InlineData("8101", typeof(BaseQuery), 2)]
    [InlineData("811A80000000", typeof(GlobalQuery), 6)]
    [InlineData("8120", typeof(BaseQuery), 2)]
    [InlineData("8201F6", typeof(BaseQuery), 3)]
    [InlineData("8201F7", typeof(BaseQuery), 3)]
    [InlineData("82018101", typeof(BaseQuery), 4)]
    [InlineData("82018220F6", typeof(BaseQuery), 5)]
    [InlineData("82018220F5", typeof(GlobalQuery), 2)]
    [InlineData("820182208220F6", typeof(BaseQuery), 7)]
    [InlineData("820182208220F5", typeof(GlobalQuery), 2)]
    [InlineData("82182200", typeof(GlobalQuery), 3)] // Pallas/Consensus: GetLedgerPeerSnapshot(All)
    [InlineData("82008200820682182200", typeof(BaseQuery), 9)] // Conway block-query envelope
    [InlineData("820680", typeof(UtxoByAddressQuery), 3)]
    [InlineData("820F80", typeof(UtxoByAddressQuery), 3)]
    [InlineData("82069FFF", typeof(UtxoByAddressQuery), 4)]
    [InlineData("820F9FFF", typeof(UtxoByAddressQuery), 4)]
    [InlineData("9F01FF", typeof(GlobalQuery), 3)]
    [InlineData("9F03F6FF", typeof(BaseQuery), 4)]
    [InlineData("9F03F7FF", typeof(BaseQuery), 4)] // Intentional: X9 omitted the break after undefined.
    [InlineData("8301810100", typeof(BaseQuery), 4)]
    [InlineData("820681420102", typeof(UtxoByAddressQuery), 6)]
    [InlineData("820181420102", typeof(UtxoByAddressQuery), 6)]
    [InlineData("820F81825820000000000000000000000000000000000000000000000000000000000000000000", typeof(UtxoByTxInQuery), 39)]
    [InlineData("820F829F0000FF820001", typeof(UtxoByTxInQuery), 10)] // The next TxIn requires advancing past the first TxIn break.
    public void LegacyQueryOverlapIsUnchanged(string hex, Type expectedType, int expectedConsumed)
    {
        byte[] bytes = Convert.FromHexString(hex);
        IQueryReq chosen = ReadWithoutDecodeThrows(IQueryReq.Read, bytes, out int consumed);
        Assert.IsType(expectedType, chosen);
        Assert.Equal(expectedConsumed, consumed);
        Assert.Equal(bytes[..expectedConsumed], chosen.Raw.ToArray());
        Assert.Equal(bytes[0] == 0x9F, chosen.IsIndefinite);
    }

    [Fact]
    public void DeepQueryFailsAtAnOffsetWithinTheRoot()
    {
        byte[] bytes = new byte[200_002];
        for (int offset = 0; offset < bytes.Length - 2; offset += 2)
        {
            bytes[offset] = 0x82;
        }
        bytes[^2] = 0x81;
        bytes[^1] = 0x01;
        CborDecodeException error = Assert.Throws<CborDecodeException>(() => IQueryReq.Read(bytes, out _));
        Assert.InRange(error.Offset, 1, bytes.Length - 3);
        Assert.Equal(0x82, bytes[error.Offset]);
        Assert.Contains("query nesting", error.Expected, StringComparison.Ordinal);
    }

    [Fact]
    public void PeerSnapshotRemainsAPrefixReadUntilQr1()
    {
        GlobalQuery direct = Assert.IsType<GlobalQuery>(IQueryReq.Read(Convert.FromHexString("82182200")));
        Assert.Equal(34UL, direct.Query);
        Assert.Equal(Convert.FromHexString("821822"), CborSerializer.Serialize(direct));

        BaseQuery block = Assert.IsType<BaseQuery>(IQueryReq.Read(Convert.FromHexString("82008200820682182200")));
        Assert.Equal(0, block.Query);
        BaseQuery shelley = Assert.IsType<BaseQuery>(block.InnerQuery);
        Assert.Equal(0, shelley.Query);
        BaseQuery conway = Assert.IsType<BaseQuery>(shelley.InnerQuery);
        Assert.Equal(6, conway.Query);
        GlobalQuery nested = Assert.IsType<GlobalQuery>(conway.InnerQuery);
        Assert.Equal(direct.Query, nested.Query);
        Assert.Equal(direct.Raw.ToArray(), nested.Raw.ToArray());
    }

    [Fact]
    public void NegativeSelectorAndNullNestedChildRemainReadable()
    {
        BaseQuery root = Assert.IsType<BaseQuery>(IQueryReq.Read(Convert.FromHexString("820182208220F6")));
        Assert.Equal(1, root.Query);
        BaseQuery first = Assert.IsType<BaseQuery>(root.InnerQuery);
        Assert.Equal(-1, first.Query);
        Assert.Equal(Convert.FromHexString("82208220F6"), first.Raw.ToArray());
        BaseQuery second = Assert.IsType<BaseQuery>(first.InnerQuery);
        Assert.Equal(-1, second.Query);
        Assert.Equal(Convert.FromHexString("8220F6"), second.Raw.ToArray());
        Assert.Null(second.InnerQuery);
    }

    [Theory]
    [InlineData("8203A0", typeof(N2NQueryReply))]
    [InlineData("8203A119801782182AF4", typeof(N2CQueryReply))]
    [InlineData("8203A10E84182AF500F4", typeof(N2NQueryReply))]
    public void QueryReplyFamilyComesFromWireKeys(string hex, Type family) =>
        Assert.IsType(family, HandshakeMessage.Read(Convert.FromHexString(hex), out _));

    [Fact]
    public void BareEmptyVersionTableRetainsItsDifferentLegacyTieBreak() =>
        Assert.Empty(CheckRead<VersionTable, N2CVersionTable>(VersionTable.Read, [0xA0]).Value);

    [Fact]
    public void CapturedCliCurrentEraRequestRetainsItsNestedQueryAndBytes()
    {
        const string fixture = "wire/c4-out-p7-003.cbor";
        byte[] frame = PreprodFixtures.Read(fixture);
        Assert.Equal("68405edb339d8c64f9d87213db53255ad341ccb71c6fbcc676c3dd392b1d30f9",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(frame)));
        Assert.Equal(new byte[] { 0x82, 0x03 }, frame[..2]);
        BaseQuery outer = CheckRead<IQueryReq, BaseQuery>(IQueryReq.Read, frame[2..]);
        Assert.Equal(0, outer.Query);
        Assert.Equal(2, Assert.IsType<BaseQuery>(outer.InnerQuery).Query);
    }

    [Theory]
    [InlineData("F5")]
    [InlineData("8301F5F5")]
    [InlineData("8301198017F5")]
    public async Task MalformedHandshakeIsADecodeErrorNotAPeerRefusal(string hex)
    {
        using ProtocolReplay replay = new(Chrysalis.Network.Core.ProtocolType.Handshake, Convert.FromHexString(hex));
        MiniProtocols.Handshake handshake = new(replay.Channel);
        ProposeVersions offer = HandshakeMessages.ProposeVersions(VersionTables.N2cV10AndAbove(1));
        _ = await Assert.ThrowsAnyAsync<CborDecodeException>(() =>
            handshake.SendAsync<N2CAcceptVersion>(offer, CancellationToken.None));
        Assert.False(handshake.IsDone);
    }

    [Fact]
    public async Task MalformedOutOfAgencyQueryKeepsItsDecodeError()
    {
        using ProtocolReplay replay = new(Chrysalis.Network.Core.ProtocolType.LocalStateQuery, Convert.FromHexString("8203F5"));
        await using MiniProtocols.LocalStateQuery client = new(replay.Channel);
        _ = await Assert.ThrowsAnyAsync<CborDecodeException>(() => client.AcquireAsync(null, CancellationToken.None));
        Assert.False(client.IsAcquired);
    }

    [Theory]
    [InlineData("8203A11980109F16F400FF00")]
    [InlineData("8200A119801684198010F400F5")]
    public void DirectHandshakeDecodeErrorStaysTyped(string hex) =>
        Assert.ThrowsAny<CborDecodeException>(() => HandshakeMessage.Read(Convert.FromHexString(hex), out _));

    [Theory]
    [InlineData("D8189D")]
    [InlineData("98")]
    public void DirectHeaderDecodeErrorStaysTyped(string hex) =>
        Assert.ThrowsAny<CborDecodeException>(() => INetworkBlockHeader.Read(Convert.FromHexString(hex), out _));

    [Theory]
    [InlineData("8204A0", 1)]
    [InlineData("D8188204A0", 3)]
    [InlineData("83011A000100008201F4", 2)]
    [InlineData("8203A1208201F4", 3)]
    public void HandshakeInvalidIndexOrVersionPointsToItsToken(string hex, int offset) =>
        Assert.Equal(offset, Assert.Throws<CborDecodeException>(() => HandshakeMessage.Read(Convert.FromHexString(hex), out _)).Offset);

    [Theory]
    [InlineData("A1208201F4", 1)]
    [InlineData("A11A000100008201F4", 1)]
    public void VersionTableInvalidVersionPointsToItsKey(string hex, int offset) =>
        Assert.Equal(offset, Assert.Throws<CborDecodeException>(() => VersionTable.Read(Convert.FromHexString(hex), out _)).Offset);

    [Fact]
    public void StandaloneHeaderDecodeErrorStaysTyped() =>
        _ = Assert.ThrowsAny<CborDecodeException>(() => CborSerializer.Deserialize<INetworkBlockHeader>(Convert.FromHexString("D81844")));

    [Fact]
    public async Task AcquireStillRejectsAnOutOfAgencyQuery()
    {
        byte[] message = Convert.FromHexString("82038101");
        using ProtocolReplay replay = new(Chrysalis.Network.Core.ProtocolType.LocalStateQuery, message);
        await using MiniProtocols.LocalStateQuery client = new(replay.Channel);
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.AcquireAsync(null, CancellationToken.None));
        Assert.StartsWith("Failed to acquire state:", error.Message, StringComparison.Ordinal);
        Assert.False(client.IsAcquired);
    }

    [Fact]
    public void ByronHeaderPrefixAndEncodedBodyArePreserved()
    {
        ByronN2NHeader header = CheckRead<INetworkBlockHeader, ByronN2NHeader>(INetworkBlockHeader.Read,
            Convert.FromHexString("82820003D81843820102"));
        Assert.Equal(new ulong[] { 0, 3 }, header.Prefix.Value);
        Assert.Equal(Convert.FromHexString("820102"), header.Header.Value.ToArray());
    }

    [Theory]
    [InlineData("43820102", typeof(ShelleyN2NHeader))]
    [InlineData("D81943820102", typeof(ShelleyN2NHeader))]
    [InlineData("D818D81843820102", typeof(ShelleyN2NHeader))]
    public void HeaderKindDoesNotTightenAcceptedTagSpellings(string hex, Type family)
    {
        byte[] bytes = Convert.FromHexString(hex);
        INetworkBlockHeader chosen = ReadWithoutDecodeThrows(INetworkBlockHeader.Read, bytes, out int consumed);
        Assert.IsType(family, chosen);
        Assert.Equal(bytes.Length, consumed);
        Assert.Equal(bytes, chosen.Raw.ToArray());
    }

    private static TCase CheckRead<TUnion, TCase>(Read<TUnion> read, byte[] original)
        where TUnion : ICborType
        where TCase : TUnion
    {
        byte[] framed = [.. original, 0];
        TUnion result = ReadWithoutDecodeThrows(read, framed, out int consumed);
        Assert.Equal(original.Length, consumed);
        TCase selected = Assert.IsType<TCase>(result);
        Assert.Equal(original, selected.Raw.ToArray());
        return selected;
    }

    private static T ReadWithoutDecodeThrows<T>(Read<T> read, byte[] data, out int consumed)
    {
        _ = read(data, out _);
        int thread = Environment.CurrentManagedThreadId;
        int throws = 0;
        void Observe(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (Environment.CurrentManagedThreadId == thread && CborError.IsDecodeFailure(args.Exception))
            {
                throws++;
            }
        }
        T result;
        AppDomain.CurrentDomain.FirstChanceException += Observe;
        try
        {
            result = read(data, out consumed);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= Observe;
        }
        Assert.Equal(0, throws);
        return result;
    }
}
