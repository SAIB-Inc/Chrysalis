using System.Security.Cryptography;
using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Xunit;

namespace Chrysalis.Network.Tests;

// D17 provenance (Apache-2.0): known-answer literals from Pallas v1.5.0-rc.2 (daecef59) queries_v16/codec.rs:958-994;
// derived vectors from Consensus 5.4.0.0 (808e4005) Shelley/Ledger/Query.hs:193-209,721-809,
// HFC SerialiseNodeToClient.hs:433-453, HFC Ledger/Query.hs:557-569, SingleEraBlock.hs:177-178,
// and Pallas v1.5.0-rc.2 (daecef59) queries_v16/{mod.rs:57-71,codec.rs:97-103,989-993}; synthetic controls labelled apart.
public sealed class Qr1QueryArgumentTests
{
    private const string PoolHash = "11111111111111111111111111111111111111111111111111111111";

    [Theory]
    [InlineData("811822", "833d7bfe8e02a697332622b3b32f9e06609d71a18839b615084b19fc2fcb9d3e", typeof(BaseQuery), false)] // Known-answer Pallas [34]
    [InlineData("82182200", "245c1068db190f29a065a5db9f005cbf07aa3c425603fae8392834ebd982375c", typeof(GlobalQuery), true)] // Known-answer Pallas [34,0]
    [InlineData("82182201", "859da9b6e28079bd7bd460229144753adbff8f9069570707d9c53d317437fbca", typeof(GlobalQuery), true)] // Known-answer Pallas [34,1]
    [InlineData("82182480", "f38018b1b9e3ff734059d6155b8dc32d65d71e9123df3ef76b958b9048c77c59", typeof(UtxoByAddressQuery), false)] // Known-answer Pallas tag 36 None
    [InlineData("821827d9010280", "947739f212dd4f693af6b3c2bdfddcb52462db09f8fef0eb97ee43cc1039af23", typeof(UtxoByAddressQuery), false)] // Known-answer Pallas tag 39 empty
    [InlineData("82008200820682182200", "233297438c8fd4585d756ab824ad678ab74cf14cb7697bc3760ec89754153231", typeof(BaseQuery), false)] // Derived Conway All
    [InlineData("82008200820682182201", "8b2b738b7b8ecb1818f81ce78ea3c292d750254271697c44896f3bcfe941b95a", typeof(BaseQuery), false)] // Derived Conway Big
    [InlineData("820082008206811822", "692487e5d8df5ee7ba28064ad6817f1ba3acf971e0be555465a961f8b84238ad", typeof(BaseQuery), false)] // Derived Conway [34]
    [InlineData("84181bd9010280d9010280d901028100", "6d373ec472fcc979b95bd52a4bde878f75644a3ed654732f30b754d50fef8e24", typeof(GlobalQuery), true)] // Derived tag 27 3 tagged sets
    [InlineData("8301810006", "fa10f10387c36d300f41aa2abbc3a4bc98f3c6089894c37138147caabbe7ac98", typeof(GlobalQuery), true)] // Derived QueryAnytime
    [InlineData("82008301810006", "2d511477bd2bb4939cc83ec4eefde5e7efe7b25cc0e19e145bd9087589ef71b6", typeof(BaseQuery), false)] // Derived LedgerQuery Anytime
    [InlineData("82182481d9010281581c" + PoolHash, "b68e8f7b15dde0e1bac88c4a332e16941de7e450004655d12f0bf106baa3ac62", typeof(GlobalQuery), true)] // Derived tag 36 Some
    [InlineData("8211d9010281581c" + PoolHash, "43ec0ad3b6334ab34c155135f1ef00b082691b17f0002e59459a2d7589442a1f", typeof(UtxoByAddressQuery), false)] // Derived tag 17 plain pool set
    [InlineData("821381d9010281581c" + PoolHash, "c53ced475a05b0fbd88d22db279d673c8e34326b86708dca2b16e9445c905d2b", typeof(GlobalQuery), true)] // Derived tag 19 Maybe pool set
    [InlineData("820982182200", "619075b85071e079547f4ae98c63badaacc2b281964f872c203e6369dfc46498", typeof(BaseQuery), false)] // Derived tag 9 GetCBOR
    public void UpstreamQueryVectorsRoundTripWithoutArgumentLoss(string hex, string sha256, Type expectedType, bool checkDirectGlobal)
    {
        byte[] bytes = Hex(hex);
        Assert.Equal(sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        _ = CheckQuery(bytes, expectedType);
        if (checkDirectGlobal)
        {
            GlobalQuery direct = X33UnionDispatchTests.CheckRead<GlobalQuery, GlobalQuery>(GlobalQuery.Read, bytes);
            Assert.Equal(bytes, CborSerializer.Serialize(direct with { Raw = default }));
            Assert.Equal(bytes, CborSerializer.Serialize<IQueryReq>(direct with { Raw = default }));
            Assert.Equal(bytes, CborSerializer.Serialize(CborSerializer.Deserialize<GlobalQuery>(bytes)));
        }
    }

    [Fact]
    public void QueryMessageNestedWrappersAndDeepChainPreserveArgumentsWhenRawIsCleared()
    {
        byte[] msgBytes = Hex("820382008200820682182200");
        Assert.Equal("cb1d247121480ccd4e2e6aab4f16a7608cbefcba83692c11ed764f7d753c8a4a", Convert.ToHexStringLower(SHA256.HashData(msgBytes)));
        Query msg = X33UnionDispatchTests.CheckRead<LocalStateQueryMessage, Query>(LocalStateQueryMessage.Read, msgBytes);
        Assert.Equal(3, msg.Idx);
        Assert.Equal(msgBytes, CborSerializer.Serialize(msg));
        ClearRaw(msg);
        Assert.Equal(msgBytes, CborSerializer.Serialize(msg));
        foreach (string hex in new[] { "820982182200", "82008301810006" })
        {
            byte[] bytes = Hex(hex);
            BaseQuery nested = X33UnionDispatchTests.CheckRead<IQueryReq, BaseQuery>(IQueryReq.Read, bytes);
            _ = Assert.IsType<GlobalQuery>(nested.InnerQuery);
            ClearRaw(nested);
            Assert.Equal(bytes, CborSerializer.Serialize(nested));
        }
        const int depth = 64;
        byte[] deep = new byte[(depth * 2) + 4];
        for (int i = 0; i < depth * 2; i += 2) { deep[i] = 0x82; }
        Hex("82182200").CopyTo(deep.AsSpan(depth * 2));
        IQueryReq root = CheckQuery(deep, typeof(BaseQuery)), cursor = root;
        for (int i = 0; i < depth; i++) { cursor = Assert.IsType<BaseQuery>(cursor).InnerQuery!; }
        Assert.Equal(34UL, Assert.IsType<GlobalQuery>(cursor).Query);
        ClearRaw(root);
        Assert.Equal(deep, CborSerializer.Serialize(root));
    }

    [Fact]
    public void GlobalQueryApiParityAndLegacyPrefixControlsHold()
    {
        GlobalQuery constructed = new(Query: 34UL);
        constructed.Deconstruct(out ulong selector);
        Assert.Equal(34UL, selector);
        Assert.Equal(Hex("811822"), CborSerializer.Serialize(constructed));
        Assert.Equal(Hex("811822"), CborSerializer.Serialize(GlobalQuery.Create(query: 34UL)));
        Assert.Equal(Hex("9f1822ff"), CborSerializer.Serialize(new GlobalQuery(34UL) { IsIndefinite = true }));

        GlobalQuery truncated = Assert.IsType<GlobalQuery>(X33UnionDispatchTests.ReadWithoutDecodeThrows(IQueryReq.Read, Hex("821822"), out int truncatedConsumed));
        Assert.Equal(3, truncatedConsumed);
        Assert.Equal(Hex("811822"), CborSerializer.Serialize(truncated with { Raw = default }));
        GlobalQuery prefix = Assert.IsType<GlobalQuery>(X33UnionDispatchTests.ReadWithoutDecodeThrows(IQueryReq.Read, Hex("9f0100ff"), out int consumed));
        Assert.Equal(2, consumed);
        Assert.True(prefix.IsIndefinite);
        Assert.Equal(Hex("9f01ff"), CborSerializer.Serialize(prefix with { Raw = default }));

        foreach (string hex in new[] { "8218229f0001ff", "82069f0102ff" }) // Synthetic: opaque indefinite arguments.
        {
            byte[] bytes = Hex(hex);
            Assert.Equal(bytes, CborSerializer.Serialize(X33UnionDispatchTests.CheckRead<IQueryReq, GlobalQuery>(IQueryReq.Read, bytes) with { Raw = default }));
            Assert.Equal(bytes, CborSerializer.Serialize(X33UnionDispatchTests.CheckRead<GlobalQuery, GlobalQuery>(GlobalQuery.Read, bytes) with { Raw = default }));
        }
        foreach (string invalid in new[] { "", "F5", "80", "8120" }) { _ = Assert.ThrowsAny<CborDecodeException>(() => GlobalQuery.Read(Hex(invalid), out _)); }
    }

    [Fact]
    public void SyntheticIndefiniteLevelsInsideDefiniteFramesKeepTypedListsAndNoRescanOpaqueGlobal()
    {
        BaseQuery conway = Assert.IsType<BaseQuery>(CheckQuery(Hex("8200820082069F0681420102FF"), typeof(BaseQuery)));
        UtxoByAddressQuery conwayAddr = Assert.IsType<UtxoByAddressQuery>(Assert.IsType<BaseQuery>(Assert.IsType<BaseQuery>(conway.InnerQuery).InnerQuery).InnerQuery);
        Assert.True(conwayAddr.IsIndefinite);
        BaseQuery negAddr = Assert.IsType<BaseQuery>(CheckQuery(Hex("82209F0681420102FF"), typeof(BaseQuery)));
        Assert.True(Assert.IsType<UtxoByAddressQuery>(negAddr.InnerQuery).IsIndefinite);
        BaseQuery negTxIn = Assert.IsType<BaseQuery>(CheckQuery(Hex("82209F0F81820000FF"), typeof(BaseQuery)));
        Assert.True(Assert.IsType<UtxoByTxInQuery>(negTxIn.InnerQuery).IsIndefinite);
        foreach (string hex in new[] { "82009F0082182200FF", "82009F068000FF" })
        {
            byte[] bytes = Hex(hex);
            GlobalQuery opaque = Assert.IsType<GlobalQuery>(CheckQuery(bytes, typeof(GlobalQuery)));
            Assert.Equal(bytes, CborSerializer.Serialize(opaque with { Raw = default }));
        }
    }

    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    private static IQueryReq CheckQuery(byte[] original, Type expectedType)
    {
        IQueryReq chosen = X33UnionDispatchTests.ReadWithoutDecodeThrows(IQueryReq.Read, [.. original, 0], out int consumed);
        Assert.Equal(original.Length, consumed);
        Assert.IsType(expectedType, chosen);
        Assert.Equal(original, chosen.Raw.ToArray());
        Assert.Equal(original, CborSerializer.Serialize(chosen));
        return chosen;
    }

    private static void ClearRaw(object node)
    {
        if (node is Query q) { q.Raw = default; ClearRaw(q.QueryRequest); }
        else if (node is BaseQuery b) { b.Raw = default; if (b.InnerQuery is not null) { ClearRaw(b.InnerQuery); } }
        else if (node is GlobalQuery g) { g.Raw = default; }
    }
}
