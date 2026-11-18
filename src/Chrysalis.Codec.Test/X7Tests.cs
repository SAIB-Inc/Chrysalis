using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Byron;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Codec.Test;

public class X7Tests
{
    [Theory]
    [InlineData("01")]
    [InlineData("1BFFFFFFFFFFFFFFFF")]
    [InlineData("20")]
    [InlineData("3B7FFFFFFFFFFFFFFF")]
    [InlineData("3BFFFFFFFFFFFFFFFF")]
    [InlineData("C24101")]
    [InlineData("C34101")]
    [InlineData("D87980")]
    [InlineData("D87A8101")]
    [InlineData("D9050080")]
    [InlineData("D86682188080")]
    [InlineData("815F4101FF")]
    public void X7ValidPlutusDataDoesNotThrowDuringDispatch(string hex)
    {
        byte[] raw = Convert.FromHexString(hex);
        int thread = Environment.CurrentManagedThreadId;
        int throws = 0;
        void CountThrow(object? _, FirstChanceExceptionEventArgs __)
        {
            if (Environment.CurrentManagedThreadId == thread)
            {
                throws++;
            }
        }

        IPlutusData result;
        int consumed;
        AppDomain.CurrentDomain.FirstChanceException += CountThrow;
        try
        {
            result = IPlutusData.Read(raw, out consumed);
            if (result is PlutusList list)
            {
                foreach (IPlutusData item in list.Value.GetValue())
                {
                    _ = item;
                }
            }
            if (result is PlutusConstr constr)
            {
                foreach (IPlutusData item in constr.Fields.GetValue())
                {
                    _ = item;
                }
            }
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= CountThrow;
        }
        Assert.NotNull(result);
        Assert.Equal(raw.Length, consumed);
        Assert.Equal(0, throws);
    }

    [Fact]
    public void X7OversizedExtendedConstructorHasTypedIndexError()
    {
        byte[] data = Convert.FromHexString("D866821A8000000080");
        CborDecodeException error = Assert.Throws<CborDecodeException>(() => IPlutusData.Read(data));
        Assert.Equal(3, error.Offset);
        Assert.Equal("constructor index out of range for Int32", error.Expected);
    }

    [Fact]
    public void X7GenericMapFreshWriterKeepsDuplicatePairs()
    {
        byte[] raw = Convert.FromHexString("A201020103");
        CborMap map = CborMap.Read(raw);
        Assert.Equal(2, map.Value.Count);
        Assert.Equal(raw, CborSerializer.Serialize(CborMap.Create(map.Value)));
    }

    [Fact]
    public void X7PlutusMapFreshWriterKeepsDuplicatePairs()
    {
        JsonElement golden = ReadPallasGolden("datum");
        byte[] raw = Convert.FromHexString(golden.GetProperty("cborHex").GetString()!);
        AssertPallasInput(golden, raw);
        PlutusMap map = PlutusMap.Read(raw);
        Assert.Equal(PallasPairs(golden), map.Value.Select(pair =>
            (Convert.ToHexStringLower(pair.Key.Raw.Span), Convert.ToHexStringLower(pair.Value.Raw.Span))));
        Assert.Equal(raw, CborSerializer.Serialize(PlutusMap.Create(map.Value)));
    }

    [Fact]
    public void X7MetadataMapFreshWriterKeepsDuplicatePairs()
    {
        byte[] raw = Convert.FromHexString("A2616101616102");
        MetadatumMap map = MetadatumMap.Read(raw);
        Assert.Equal(2, map.Value.Count);
        Assert.Equal(raw, CborSerializer.Serialize(MetadatumMap.Create(map.Value)));
    }

    [Fact]
    public void X7KeyedRecordCallerGetsTypedErrorAtRepeatedField()
    {
        byte[] tx = ReadGolden("duplicate-fee");
        CborDecodeException error = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<BabbageTransactionBody>(tx.AsMemory(1)));
        Assert.Equal(76, error.Offset);
        Assert.Equal("2", error.Actual);
    }

    [Fact]
    public void X7EagerKeyedRecordRejectsAnEquivalentKeyEncoding()
    {
        byte[] data = Convert.FromHexString("A20101180102");
        CborDecodeException direct = Assert.Throws<CborDecodeException>(() => X7KeyedEager.Read(data));
        Assert.Equal(3, direct.Offset);
        Assert.Equal("1", direct.Actual);
        _ = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<X7KeyedEager>(data));
    }

    [Theory]
    [InlineData("duplicate-mint-babbage-fallback")]
    [InlineData("duplicate-mint-conway")]
    public void X7LedgerMintKeepsTheLastPair(string id)
    {
        byte[] tx = ReadGolden(id);
        MultiAssetMint? mint = id == "duplicate-mint-conway"
            ? ConwayTransactionBody.Read(tx.AsMemory(1)).Mint
            : BabbageTransactionBody.Read(tx.AsMemory(1)).Mint;
        Assert.True(mint.HasValue);
        Assert.Equal(2L, Assert.Single(Assert.Single(mint.GetValueOrDefault().Value).Value.Value).Value);
        Assert.Equal(id == "duplicate-mint-conway" ? 1 : 0, CliGolden(id).GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void X7NestedLedgerKeyEqualityUsesDecodedValue()
    {
        byte[] data = Convert.FromHexString("A282000082018201008200008202820200");
        RedeemerMap redeemers = RedeemerMap.Read(data);
        Assert.Equal(2UL, Assert.Single(redeemers.Value).Value.ExUnits.Mem);
    }

    [Fact]
    public void X7EraSelectionDoesNotGuessTheTransactionShape()
    {
        byte[] data = ReadGolden("ledger-conway");
        PostMaryTransaction tx = Assert.IsType<PostMaryTransaction>(ITransaction.Read(data, Era.Conway));
        X7ConwayProjection.Check(tx, data, CliGolden("ledger-conway").GetProperty("stdout"));
        CborDecodeException missingEra = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<ITransaction>(data));
        Assert.Equal("explicit transaction era", missingEra.Expected);
    }

    [Fact]
    public void X7ConwayPreprodReadsWithTheExplicitEra()
    {
        byte[] raw = ReadGolden("g1-conway");
        PostMaryTransaction tx = Assert.IsType<PostMaryTransaction>(ITransaction.Read(raw, Era.Conway, out int consumed));
        Assert.Equal(raw.Length, consumed);
        X7ConwayProjection.Check(tx, raw, CliGolden("g1-conway").GetProperty("stdout"));
        Assert.Equal(raw, CborSerializer.Serialize(tx));
        CborDecodeException error = Assert.Throws<CborDecodeException>(() => ITransaction.Read(ReadGolden("ledger-conway"), Era.Alonzo));
        Assert.Equal((84, "18", "field key valid for Alonzo"), (error.Offset, error.Actual, error.Expected));
    }

    [Fact]
    public void X7PreConwayListRetainsBothEqualSetElements()
    {
        string input = "825820" + new string('0', 64) + "00";
        byte[] raw = Convert.FromHexString("A30082" + input + input + "01800201");
        BabbageTransactionBody body = BabbageTransactionBody.Read(raw);
        TransactionInput[] restored = [.. body.Inputs.GetValue()];
        Assert.Equal(2, restored.Length);
        Assert.Equal(restored[0].Raw.ToArray(), restored[1].Raw.ToArray());
        Assert.Equal(raw, CborSerializer.Serialize(body));
    }

    [Theory]
    [InlineData("00", true)]
    [InlineData("1A80000000", true)]
    [InlineData("1B7FFFFFFFFFFFFFFF", true)]
    [InlineData("1BFFFFFFFFFFFFFFFF", true)]
    [InlineData("20", false)]
    public void MetadataIntegersRetainTheirBaseCase(string hex, bool nonNegative)
    {
        byte[] data = Convert.FromHexString(hex);
        Type expected = nonNegative ? typeof(MetadatumIntUlong) : typeof(MetadatumIntLong);
        Assert.IsType(expected, IMetadatumInt.Read(data));
        Assert.IsType(expected, ITransactionMetadatum.Read(data));
    }

    [Theory]
    [InlineData("A206000601", 3)]
    [InlineData("BF06000601FF", 3)]
    public void X7LazyRecordRejectsRepeatedUnknownKey(string hex, int offset)
    {
        CborDecodeException error = Assert.Throws<CborDecodeException>(() => ConwayTransactionBody.Read(Convert.FromHexString(hex)));
        Assert.Equal((offset, "6", "unique field key"), (error.Offset, error.Actual, error.Expected));
    }

    [Fact]
    public void X7RewardAccountEquality() => Assert.Equal(2UL, Assert.Single(Withdrawals.Read(Convert.FromHexString("A241AA0141AA02")).Value).Value);
    [Fact]
    public void X7CredentialEquality() => Assert.Equal(2L, Assert.Single(StakeCredentials.Read(Convert.FromHexString("A2820041AA01820041AA02")).Value).Value);
    [Fact]
    public void X7VoterEquality() => _ = Assert.Single(Assert.Single(VotingProcedures.Read(Convert.FromHexString("A2820041AAA0820041AAA18241BB008201F6")).Value).Value.Value);
    [Fact]
    public void X7GovActionIdEquality() => Assert.Equal(2, Assert.Single(GovActionIdVotingProcedure.Read(Convert.FromHexString("A28241AA008201F68241AA008202F6")).Value).Value.Vote);

    [Theory]
    [InlineData("84A200800080A0F5F6", 4)]
    [InlineData("84A0A200800080F5F6", 5)]
    public void X7ExplicitEraRejectsDuplicatesBeforeReturning(string hex, int offset)
    {
        CborDecodeException error = Assert.Throws<CborDecodeException>(() => ITransaction.Read(Convert.FromHexString(hex), Era.Babbage));
        Assert.Equal((offset, "0", "unique field key"), (error.Offset, error.Actual, error.Expected));
    }

    [Fact]
    public void X7ExplicitEraChecksEnvelopeArity() => Assert.Equal("transaction envelope of 4 fields",
        Assert.Throws<CborDecodeException>(() => ITransaction.Read(Convert.FromHexString("83A0A0F6"), Era.Alonzo)).Expected);

    [Fact]
    public void X7ExplicitOlderEraBindsBodyAndWitnesses()
    {
        byte[] raw = Convert.FromHexString("84A3008001800201A0F5F6");
        PostMaryTransaction tx = Assert.IsType<PostMaryTransaction>(ITransaction.Read(raw, Era.Alonzo));
        _ = Assert.IsType<AlonzoTransactionBody>(tx.Body);
        _ = Assert.IsType<AlonzoTransactionWitnessSet>(tx.Witnesses);
        _ = Assert.IsType<ConwayTransactionBody>(PostMaryTransaction.Read(raw).Body);
    }

    [Fact]
    public void X7EraLessBodyKeySixChoosesAnOlderBody() =>
        _ = Assert.IsType<AlonzoTransactionBody>(PostMaryTransaction.Read(Convert.FromHexString("84A10682A000A0F5F6")).Body);

    [Theory]
    [InlineData("A10C00")]
    [InlineData("A10D00")]
    [InlineData("A10E00")]
    [InlineData("A10F00")]
    public void X7ProtocolUpdateReadsOldKey(string hex) =>
        _ = Assert.IsType<BabbageProtocolParamUpdate>(IProtocolParamUpdate.Read(Convert.FromHexString(hex)));

    [Fact]
    public void X7ProtocolUpdateRefusesMixedKeys() => Assert.Equal("one era's update fields",
        Assert.Throws<CborDecodeException>(() => IProtocolParamUpdate.Read(Convert.FromHexString("A20E820800181901"))).Expected);

    [Fact]
    public void X7FreshPairListsKeepIndefiniteFraming()
    {
        byte[] raw = Convert.FromHexString("BF01020103FF");
        Assert.Equal(raw, CborMap.Create(CborMap.Read(raw).Value).Raw.ToArray());
        Assert.Equal(raw, MetadatumMap.Create(MetadatumMap.Read(raw).Value).Raw.ToArray());
        Assert.Equal(raw, PlutusMap.Create(PlutusMap.Read(raw).Value).Raw.ToArray());
    }

    [Fact]
    public void X7RuntimeAmbiguityNamesCasesAndContextualReaders()
    {
        CborDecodeException overlap = Assert.Throws<CborDecodeException>(() => IX7Overlap.Read(Convert.FromHexString("8101")));
        Assert.Contains("X7Required, X7Optional", overlap.Expected, StringComparison.Ordinal);
        _ = Assert.IsType<X7Optional>(IX7Overlap.Read(Convert.FromHexString("81F6")));
        Assert.Contains("MultiAssetMint.Read", Assert.Throws<CborDecodeException>(() => IMultiAsset.Read(Convert.FromHexString("A0"))).Expected, StringComparison.Ordinal);
        Assert.Contains("TokenBundleMint.Read", Assert.Throws<CborDecodeException>(() => ITokenBundle.Read(Convert.FromHexString("A0"))).Expected, StringComparison.Ordinal);
        Assert.Contains("BlockWithEra.Read", Assert.Throws<CborDecodeException>(() => IBlock.Read(BlockFixture("shelley1.block").Block.Raw)).Expected, StringComparison.Ordinal);
        Assert.Contains("X7MapA, X7MapB", Assert.Throws<CborDecodeException>(() => IX7Map.Read(Convert.FromHexString("A10101"))).Expected, StringComparison.Ordinal);
    }

    [Fact]
    public void X7ByronMainBlockIsNotRejectedByTheOpaqueProofShape() => _ = Assert.IsType<ByronMainBlock>(IBlock.Read(BlockFixture("byron1.block").Block.Raw));
    [Fact]
    public void X7TaggedOrLargeGenericPrimitive() { Assert.Equal(2_147_483_648L, Assert.IsType<CborLong>(ICborPrimitive.Read(Convert.FromHexString("1A80000000"))).Value); _ = Assert.Throws<CborDecodeException>(() => ICborPrimitive.Read(Convert.FromHexString("C18101"))); }
    [Fact]
    public void X7PlutusRejectsAnUnrelatedSemanticTag() => _ = Assert.Throws<CborDecodeException>(() => IPlutusData.Read(Convert.FromHexString("D9010280")));

    [Fact]
    public void X7MalformedTupleScanStaysTypedForDirectCallers()
    {
        CborDecodeException tuple = Assert.Throws<CborDecodeException>(() => IX6DepthOne.Read(Convert.FromHexString("82016261")));
        Assert.Contains("X6DepthOneB.Value", tuple.TypePath, StringComparison.Ordinal);
        Assert.Equal(3, tuple.Offset);
        _ = Assert.IsType<SAIB.Cbor.CborException>(tuple.InnerException);
    }

    [Fact]
    public void X7MalformedConstructorScanStaysTypedForDirectCallers() =>
        _ = Assert.Throws<CborDecodeException>(() => PlutusConstr.Read(Convert.FromHexString("D866")));

    [Fact]
    public void X7ConstructorArityErrorPointsAtArray() =>
        Assert.Equal(3, Assert.Throws<CborDecodeException>(() => PlutusConstr.Read(Convert.FromHexString("D9006680"))).Offset);

    [Fact]
    public void X7OptionalTrailingFieldDoesNotBypassAmbiguity()
    {
        byte[] shared = [0x81, 1];
        Assert.Equal(1, X7Short.Read(shared).Value);
        Assert.Equal(1, X7Long.Read(shared).Value);
        CborDecodeException error = Assert.Throws<CborDecodeException>(() => IX7Range.Read(shared));
        Assert.Contains("X7Short", error.Expected, StringComparison.Ordinal);
        Assert.Contains("X7Long", error.Expected, StringComparison.Ordinal);
        Assert.Equal(2, Assert.IsType<X7Long>(IX7Range.Read((byte[])[0x82, 1, 2])).Tail);
    }

    private static BlockWithEra BlockFixture(string name) => BlockWithEra.Read(Convert.FromHexString(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name)).Trim()));

    internal static JsonElement ReadPallasGolden(string id)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "X7PallasGoldens.json")));
        return manifest.RootElement.GetProperty(id).Clone();
    }

    internal static void AssertPallasInput(JsonElement row, ReadOnlySpan<byte> data) =>
        Assert.Equal(row.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)));

    internal static IEnumerable<(string Key, string Value)> PallasPairs(JsonElement row) =>
        row.GetProperty("pairs").EnumerateArray().Select(pair => (pair[0].GetString()!, pair[1].GetString()!));

    private static JsonElement CliGolden(string id)
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "X7CliGoldens.json")));
        return manifest.RootElement.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("id").GetString() == id).Clone();
    }

    private static byte[] ReadGolden(string id)
    {
        JsonElement row = CliGolden(id);
        byte[] data = Convert.FromHexString(row.GetProperty("cborHex").GetString()!);
        Assert.Equal(row.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(data)));
        return data;
    }
}

[CborSerializable, CborMap]
public partial record X7KeyedEager([CborProperty(1)] int Value) : CborRecord;

[CborSerializable, CborUnion] public partial interface IX7Overlap : ICborType;
[CborSerializable, CborList] public readonly partial record struct X7Required : IX7Overlap { [CborOrder(0)] public partial int Value { get; } }
[CborSerializable, CborList] public readonly partial record struct X7Optional : IX7Overlap { [CborOrder(0)] public partial int? Value { get; } }
[CborSerializable, CborUnion] public partial interface IX7Map : ICborType;
[CborSerializable, CborMap] public readonly partial record struct X7MapA : IX7Map { [CborProperty(1)] public partial int Value { get; } }
[CborSerializable, CborMap] public readonly partial record struct X7MapB : IX7Map { [CborProperty(2)] public partial int Value { get; } }

[CborSerializable, CborUnion] public partial interface IX7Range : ICborType;
[CborSerializable, CborList] public readonly partial record struct X7Short : IX7Range { [CborOrder(0)] public partial int Value { get; } }
[CborSerializable, CborList] public readonly partial record struct X7Long : IX7Range { [CborOrder(0)] public partial int Value { get; } [CborOrder(1)] public partial int? Tail { get; } }
