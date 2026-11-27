using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Tx.Extensions;
using Chrysalis.Wallet.Models.Enums;
using Xunit;
using Acceptance = AikenLang.AcceptanceTest079.Blueprint;
using Shape = Fixtures.Shapes.Blueprint;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.Codec.Blueprint.Test;

/// <summary>
/// Verifies that the blueprint source generator produces correct types.
/// </summary>
public class BlueprintGenerationTests
{
    /// <summary>
    /// Verifies that all seven Ticket fields have the expected types and values.
    /// </summary>
    [Fact]
    public void TicketHasSevenFields()
    {
        PlutusBoundedBytes bytes = PlutusBoundedBytes.Create(new byte[] { 1 });
        Shape.IFixturesAddressCredential payment = new Shape.VerificationKey(Field0: bytes);
        Shape.Address owner = new(Payment: payment, Stake: None<Shape.IReferenced>.Create());
        Shape.FixturesAddressAsset asset = new(Policy: bytes, Name: bytes);
        Some<Shape.FixturesAddressAsset> fallback = Some<Shape.FixturesAddressAsset>.Create(asset);
        CborList<Shape.TupleByteArrayInt> routes = CborList.Create<Shape.TupleByteArrayInt>([new(Field0: bytes, Field1: PlutusInt.Create(2))]);
        CborList<ICborOption<IPlutusBigInt>> limits = CborList.Create<ICborOption<IPlutusBigInt>>([Some<IPlutusBigInt>.Create(PlutusInt.Create(3))]);
        CborList<IPlutusBigInt> weights = CborList.Create<IPlutusBigInt>([PlutusInt.Create(4)]);
        CborList<Shape.FixturesAddressAsset> assets = CborList.Create([asset]);
        Shape.Ticket ticket = new(Owner: owner, Memo: null, Fallback: fallback,
            Routes: routes, Limits: limits, Weights: weights, Assets: assets);

        Assert.Equal(owner, ticket.Owner);
        Assert.Null(ticket.Memo);
        Assert.Equal(fallback, ticket.Fallback);
        Assert.Equal(routes, ticket.Routes);
        Assert.Equal(limits, ticket.Limits);
        Assert.Equal(weights, ticket.Weights);
        Assert.Equal(assets, ticket.Assets);
        byte[] fallbackBytes = CborSerializer.Serialize<ICborOption<Shape.FixturesAddressAsset>>(fallback);
        ICborOption<Shape.FixturesAddressAsset> roundFallback = CborSerializer.Deserialize<ICborOption<Shape.FixturesAddressAsset>>(fallbackBytes);
        _ = Assert.IsType<Some<Shape.FixturesAddressAsset>>(roundFallback);
        Assert.Equal(fallbackBytes, CborSerializer.Serialize(roundFallback));
        byte[] limitsBytes = CborSerializer.Serialize(limits);
        CborList<ICborOption<IPlutusBigInt>>? roundLimits = CborSerializer.Deserialize<CborList<ICborOption<IPlutusBigInt>>?>(limitsBytes);
        Assert.NotNull(roundLimits);
        Assert.Equal(limitsBytes, CborSerializer.Serialize(roundLimits.Value));
    }

    /// <summary>
    /// Verifies that the PoolAction union includes the fieldless Pause and Resume cases.
    /// </summary>
    [Fact]
    public void PoolActionHasCasesWithoutFields()
    {
        Shape.IPoolAction pause = new Shape.Pause();
        Shape.IPoolAction resume = new Shape.Resume();

        Assert.NotNull(pause);
        Assert.NotNull(resume);
    }

    /// <summary>
    /// Verifies validator constants are generated from the blueprint.
    /// </summary>
    [Fact]
    public void ValidatorConstantsAreGenerated()
    {
        Assert.False(string.IsNullOrEmpty(Shape.MarketMarketSpend.CompiledCode));
        Assert.False(string.IsNullOrEmpty(Shape.MarketMarketSpend.Hash));
        Assert.Equal("v3", Shape.MarketMarketSpend.PlutusVersion);
    }

    [Fact]
    public void ScriptPropertyReturnsValidPlutusV3Script()
    {
        IScript script = Shape.MarketMarketSpend.Script;
        Assert.NotNull(script);
        Assert.IsType<PlutusV3Script>(script);
    }

    [Fact]
    public void ScriptHashMatchesConstant()
    {
        IScript script = Shape.MarketMarketSpend.Script;
        Assert.Equal(Shape.MarketMarketSpend.Hash, script.HashHex(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestnetAddressIsBech32()
    {
        WalletAddress addr = Shape.MarketMarketSpend.TestnetAddress;
        string bech32 = addr.ToBech32();
        Assert.StartsWith("addr_test1", bech32, StringComparison.Ordinal);
    }

    [Fact]
    public void MainnetAddressIsBech32()
    {
        WalletAddress addr = Shape.MarketMarketSpend.MainnetAddress;
        string bech32 = addr.ToBech32();
        Assert.StartsWith("addr1", bech32, StringComparison.Ordinal);
    }

    [Fact]
    public void GetAddressWithStakeKeyProducesDelegationAddress()
    {
        byte[] stakeKeyHash = new byte[28];
        WalletAddress addr = Shape.MarketMarketSpend.GetAddress(NetworkType.Testnet, stakeKeyHash);
        string bech32 = addr.ToBech32();
        Assert.StartsWith("addr_test1", bech32, StringComparison.Ordinal);
    }
}

/// <summary>
/// Tests for the owned blueprint covering the generator's type shapes.
/// </summary>
public class ShapesBlueprintTests
{
    private static PlutusBoundedBytes Bytes(byte value) => PlutusBoundedBytes.Create(new byte[] { value });

    [Fact]
    public void PoolStateHasEightFields()
    {
        Shape.TupleByteArrayByteArrayInt deposit = new(Field0: Bytes(0x01), Field1: Bytes(0x02), Field2: PlutusInt.Create(100));
        Shape.TupleByteArrayByteArrayInt quote = new(Field0: Bytes(0x03), Field1: Bytes(0x04), Field2: PlutusInt.Create(200));
        Shape.TupleTupleByteArrayByteArrayIntTupleByteArrayByteArrayInt reserves = new(Field0: deposit, Field1: quote);
        Shape.PoolState state = new(
            Id: Bytes(0x05),
            Reserves: reserves,
            Shares: PlutusInt.Create(300),
            FeeIn: PlutusInt.Create(3),
            FeeOut: PlutusInt.Create(4),
            Admin: Some<Shape.IFixturesPolicyPolicy>.Create(new Shape.After(Time: PlutusInt.Create(500))),
            OpensAt: PlutusInt.Create(100),
            ProtocolCut: PlutusInt.Create(1));

        Assert.Equal(reserves, state.Reserves);
        Assert.IsType<Some<Shape.IFixturesPolicyPolicy>>(state.Admin);
        Assert.NotNull(state.Admin);
        byte[] adminBytes = CborSerializer.Serialize(state.Admin!);
        ICborOption<Shape.IFixturesPolicyPolicy> roundAdmin = CborSerializer.Deserialize<ICborOption<Shape.IFixturesPolicyPolicy>>(adminBytes);
        _ = Assert.IsType<Some<Shape.IFixturesPolicyPolicy>>(roundAdmin);
        Assert.Equal(adminBytes, CborSerializer.Serialize(roundAdmin));
    }

    [Fact]
    public void OrderUnionHasSixVariants()
    {
        Shape.TupleByteArrayByteArrayInt amount = new(Field0: Bytes(0x01), Field1: Bytes(0x02), Field2: PlutusInt.Create(7));
        Shape.TupleTupleByteArrayByteArrayIntTupleByteArrayByteArrayInt amounts = new(Field0: amount, Field1: amount);
        Shape.IOrder[] orders =
        [
            new Shape.Delegate(Auth: new Shape.Signed(Policy: new Shape.Before(Time: PlutusInt.Create(100)))),
            new Shape.Exchange(Offer: amount, MinOut: amount),
            new Shape.Add(Amounts: amounts),
            new Shape.Remove(Shares: amount),
            new Shape.Gift(Amounts: amounts),
            new Shape.Note(Tag: new Shape.TupleByteArrayByteArray(Field0: Bytes(0x03), Field1: Bytes(0x04))),
        ];
        Assert.Collection(orders.Select(order => CborSerializer.Deserialize<Shape.IOrder>(CborSerializer.Serialize(order))),
            order => Assert.IsType<Shape.Delegate>(order),
            order => Assert.IsType<Shape.Exchange>(order),
            order => Assert.IsType<Shape.Add>(order),
            order => Assert.IsType<Shape.Remove>(order),
            order => Assert.IsType<Shape.Gift>(order),
            order => Assert.IsType<Shape.Note>(order));
    }

    [Fact]
    public void PolicyRecursiveUnionIsGenerated()
    {
        Shape.IFixturesPolicyPolicy key = new Shape.FixturesPolicyPolicyKey(Hash: Bytes(0xAB));
        Shape.IFixturesPolicyPolicy policy = new Shape.All(Policies: CborList.Create([key]));

        // Constructor 1, indefinite fields, a definite one-element list containing constructor 0 and a one-byte hash.
        Assert.Equal("D87A9F81D8799F41ABFFFF", Convert.ToHexString(CborSerializer.Serialize(policy)));
    }

    [Fact]
    public void ConfigHasTwelveFields()
    {
        Shape.IFixturesAddressCredential payment = new Shape.VerificationKey(Field0: Bytes(0x01));
        Shape.Address treasury = new(Payment: payment,
            Stake: Some<Shape.IReferenced>.Create(new Shape.Inline(Field0: payment)));
        Shape.Address payout = new(Payment: new Shape.Script(Field0: Bytes(0x02)), Stake: None<Shape.IReferenced>.Create());
        Some<CborList<PlutusBoundedBytes>> operators = Some<CborList<PlutusBoundedBytes>>.Create(CborList.Create([Bytes(0x03)]));
        Shape.Config config = new(
            Owner: new Shape.After(Time: PlutusInt.Create(100)),
            Operators: operators,
            Treasury: treasury,
            Payout: payout,
            PayoutShare: new Shape.TupleIntInt(Field0: PlutusInt.Create(1), Field1: PlutusInt.Create(10)),
            StakeKeys: CborList.Create([payment]),
            Guardian: new Shape.Before(Time: PlutusInt.Create(200)),
            FeeFloor: PlutusInt.Create(1),
            FeeStep: PlutusInt.Create(2),
            FeeCap: PlutusInt.Create(5),
            PoolFee: PlutusInt.Create(3),
            Extra: PlutusInt.Create(7));

        Assert.Equal(operators, config.Operators);
        Assert.IsType<PlutusInt>(config.Extra);
    }

    [Fact]
    public void ValidatorsAreGenerated()
    {
        Assert.NotEmpty(Shape.PoolPoolSpend.CompiledCode);
        Assert.NotEmpty(Shape.PoolPoolMint.CompiledCode);
    }

    [Fact]
    public void MarketHasTenFields()
    {
        Shape.Market market = new(
            Stake: new Shape.Pointer(Slot: PlutusInt.Create(100), Tx: PlutusInt.Create(0), Cert: PlutusInt.Create(1)),
            Base: new Shape.FixturesAddressAsset(Policy: Bytes(0x01), Name: Bytes(0x02)),
            Quote: new Shape.FixturesAddressAsset(Policy: Bytes(0x03), Name: Bytes(0x04)),
            Supply: PlutusInt.Create(400),
            BaseReserve: PlutusInt.Create(100),
            QuoteReserve: PlutusInt.Create(300),
            BaseFee: PlutusInt.Create(2),
            QuoteFee: PlutusInt.Create(3),
            ShareFee: Some<IPlutusBigInt>.Create(PlutusInt.Create(1)),
            DynamicFee: PlutusTrue.Create());

        Assert.IsType<Shape.Pointer>(market.Stake);
        IPlutusBool dynamicFee = market.DynamicFee;
        Assert.IsType<PlutusTrue>(dynamicFee);
    }

    [Fact]
    public void UnderscoredValidatorNameIsGenerated() => Assert.Equal(56, Shape.OrderBookOrderBookSpend.Hash.Length);

    [Fact]
    public void HopUnionHasThreeVariants()
    {
        Shape.Sibling sibling = new(Nibble: PlutusInt.Create(2), Prefix: Bytes(0xAB), Root: Bytes(0xCD));
        Shape.IFixturesProofPathHop[] hops =
        [
            new Shape.Wide(Depth: PlutusInt.Create(1), Siblings: Bytes(0x01)),
            new Shape.Narrow(Depth: PlutusInt.Create(2), Sibling: sibling),
            new Shape.Tip(Depth: PlutusInt.Create(3), Key: Bytes(0x02), Value: Bytes(0x03)),
        ];
        Assert.Collection(hops.Select(hop => CborSerializer.Deserialize<Shape.IFixturesProofPathHop>(CborSerializer.Serialize(hop))),
            hop => Assert.IsType<Shape.Wide>(hop),
            hop => Assert.IsType<Shape.Narrow>(hop),
            hop => Assert.IsType<Shape.Tip>(hop));
    }

    [Fact]
    public void CollidingConstructorsArePrefixed()
    {
        Shape.IFixturesPolicyPolicy policy = new Shape.FixturesPolicyPolicyKey(Hash: Bytes(0xAA));
        Shape.IAuth auth = new Shape.AuthKey(Hash: Bytes(0xBB));

        Assert.IsType<Shape.FixturesPolicyPolicyKey>(CborSerializer.Deserialize<Shape.IFixturesPolicyPolicy>(CborSerializer.Serialize(policy)));
        Assert.IsType<Shape.AuthKey>(CborSerializer.Deserialize<Shape.IAuth>(CborSerializer.Serialize(auth)));
    }
}

/// <summary>
/// Tests for the Aiken acceptance test 079 blueprint's v1.0 schema.
/// </summary>
public class Aiken079BlueprintTests
{
    [Fact]
    public void WrappedRedeemerIsConstructorOne()
    {
        Acceptance.Redeemer wrapper = new(Field0: PlutusInt.Create(42));

        Assert.Equal("D87A9F182AFF", Convert.ToHexString(CborSerializer.Serialize(wrapper)));
    }

    [Fact]
    public void PlutusV2ValidatorsAreGenerated()
    {
        Assert.Equal("v2", Acceptance.FooSpend.PlutusVersion);
        Assert.Equal("v2", Acceptance.FooMint.PlutusVersion);
        Assert.IsType<PlutusV2Script>(Acceptance.FooSpend.Script);
        Assert.IsType<PlutusV2Script>(Acceptance.FooMint.Script);
    }
}

/// <summary>
/// Tests for Crashr marketplace blueprint (Address type, Credential union).
/// </summary>
public class CrashrBlueprintTests
{
    /// <summary>
    /// Verifies Credential union and Address type are generated.
    /// </summary>
    [Fact]
    public void AddressAndCredentialAreGenerated()
    {
        CrashrIo.Marketplace.Blueprint.ICredential vk = new CrashrIo.Marketplace.Blueprint.VerificationKeyCredential(Field0: default!);
        CrashrIo.Marketplace.Blueprint.Address addr = new(PaymentCredential: vk, StakeCredential: null);

        Assert.NotNull(addr);
    }

    /// <summary>
    /// Verifies crashr validator is generated.
    /// </summary>
    [Fact]
    public void CrashrValidatorIsGenerated() => Assert.False(string.IsNullOrEmpty(CrashrIo.Marketplace.Blueprint.AskSpend.CompiledCode));
}

/// <summary>
/// CBOR serialization tests verifying blueprint-generated types match Aiken's cbor.serialise output.
/// Tests both construction via Create + serialization, and deserialization round-trips.
/// </summary>
public class CborSerializationTests
{
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToUpperInvariant();

    private static readonly byte[] VkHash = Convert.FromHexString("AABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDD");
    private static readonly byte[] ScHash = Convert.FromHexString("11223344112233441122334411223344112233441122334411223344");

    /// <summary>
    /// Construct VerificationKeyCredential via Create and verify matches Aiken CBOR.
    /// </summary>
    [Fact]
    public void CreateVerificationKeyCredential()
    {
        CborVectors.CborVectors.Blueprint.VerificationKeyCredential val =
            CborVectors.CborVectors.Blueprint.VerificationKeyCredential.Create(
                PlutusBoundedBytes.Create(VkHash));

        Assert.Equal(
            "D8799F581CAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDFF",
            Hex(CborSerializer.Serialize(val)));
    }

    /// <summary>
    /// Construct ScriptCredential via Create and verify matches Aiken CBOR.
    /// </summary>
    [Fact]
    public void CreateScriptCredential()
    {
        CborVectors.CborVectors.Blueprint.ScriptCredential val =
            CborVectors.CborVectors.Blueprint.ScriptCredential.Create(
                PlutusBoundedBytes.Create(ScHash));

        Assert.Equal(
            "D87A9F581C11223344112233441122334411223344112233441122334411223344FF",
            Hex(CborSerializer.Serialize(val)));
    }

    /// <summary>
    /// Construct Address{VK, None} via Create and verify matches Aiken CBOR.
    /// </summary>
    [Fact]
    public void CreateAddressNoStake()
    {
        CborVectors.CborVectors.Blueprint.Address val =
            CborVectors.CborVectors.Blueprint.Address.Create(
                CborVectors.CborVectors.Blueprint.VerificationKeyCredential.Create(
                    PlutusBoundedBytes.Create(VkHash)),
                None<CborVectors.CborVectors.Blueprint.ICredential>.Create());

        Assert.Equal(
            "D8799FD8799F581CAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDFFD87A80FF",
            Hex(CborSerializer.Serialize(val)));
    }

    /// <summary>
    /// Construct Address{VK, Some(Script)} via Create and verify matches Aiken CBOR.
    /// </summary>
    [Fact]
    public void CreateAddressWithStake()
    {
        CborVectors.CborVectors.Blueprint.Address val =
            CborVectors.CborVectors.Blueprint.Address.Create(
                CborVectors.CborVectors.Blueprint.VerificationKeyCredential.Create(
                    PlutusBoundedBytes.Create(VkHash)),
                Some<CborVectors.CborVectors.Blueprint.ICredential>.Create(
                    CborVectors.CborVectors.Blueprint.ScriptCredential.Create(
                        PlutusBoundedBytes.Create(ScHash))));

        Assert.Equal(
            "D8799FD8799F581CAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDFFD8799FD87A9F581C11223344112233441122334411223344112233441122334411223344FFFFFF",
            Hex(CborSerializer.Serialize(val)));
        Assert.NotNull(val.StakeCredential);
        byte[] stakeBytes = CborSerializer.Serialize(val.StakeCredential!);
        ICborOption<CborVectors.CborVectors.Blueprint.ICredential> roundStake =
            CborSerializer.Deserialize<ICborOption<CborVectors.CborVectors.Blueprint.ICredential>>(stakeBytes);
        _ = Assert.IsType<Some<CborVectors.CborVectors.Blueprint.ICredential>>(roundStake);
        Assert.Equal(stakeBytes, CborSerializer.Serialize(roundStake));
    }

    /// <summary>
    /// Construct SimpleDatum{Address{VK,None}, 1000000, #"cafe", True} via Create and verify matches Aiken.
    /// </summary>
    [Fact]
    public void CreateSimpleDatum()
    {
        CborVectors.CborVectors.Blueprint.SimpleDatum val =
            CborVectors.CborVectors.Blueprint.SimpleDatum.Create(
                CborVectors.CborVectors.Blueprint.Address.Create(
                    CborVectors.CborVectors.Blueprint.VerificationKeyCredential.Create(
                        PlutusBoundedBytes.Create(VkHash)),
                    None<CborVectors.CborVectors.Blueprint.ICredential>.Create()),
                PlutusInt.Create(1000000),
                PlutusBoundedBytes.Create(Convert.FromHexString("CAFE")),
                PlutusTrue.Create());

        Assert.Equal(
            "D8799FD8799FD8799F581CAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDFFD87A80FF1A000F424042CAFED87A80FF",
            Hex(CborSerializer.Serialize(val)));
    }

    /// <summary>
    /// Construct SimpleDatum{Address{Script,Some(VK)}, 0, #"", False} via Create and verify matches Aiken.
    /// </summary>
    [Fact]
    public void CreateSimpleDatumInactive()
    {
        CborVectors.CborVectors.Blueprint.SimpleDatum val =
            CborVectors.CborVectors.Blueprint.SimpleDatum.Create(
                CborVectors.CborVectors.Blueprint.Address.Create(
                    CborVectors.CborVectors.Blueprint.ScriptCredential.Create(
                        PlutusBoundedBytes.Create(ScHash)),
                    Some<CborVectors.CborVectors.Blueprint.ICredential>.Create(
                        CborVectors.CborVectors.Blueprint.VerificationKeyCredential.Create(
                            PlutusBoundedBytes.Create(VkHash)))),
                PlutusInt.Create(0),
                PlutusBoundedBytes.Create(ReadOnlyMemory<byte>.Empty),
                PlutusFalse.Create());

        Assert.Equal(
            "D8799FD8799FD87A9F581C11223344112233441122334411223344112233441122334411223344FFD8799FD8799F581CAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDFFFFFF0040D87980FF",
            Hex(CborSerializer.Serialize(val)));
    }

    /// <summary>
    /// Deserialization round-trip: Aiken CBOR → generated type → re-serialize matches.
    /// </summary>
    [Fact]
    public void DeserializeSimpleDatumRoundTrip()
    {
        const string aikenHex = "D8799FD8799FD8799F581CAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDFFD87A80FF1A000F424042CAFED87A80FF";
        byte[] original = Convert.FromHexString(aikenHex);

        CborVectors.CborVectors.Blueprint.SimpleDatum val =
            CborSerializer.Deserialize<CborVectors.CborVectors.Blueprint.SimpleDatum>(original);

        Assert.Equal("CAFE", Convert.ToHexString(val.Tag.Value.Span).ToUpperInvariant());
        Assert.IsType<PlutusTrue>(val.Active);
        Assert.Equal(aikenHex, Hex(CborSerializer.Serialize(val)));
    }

    /// <summary>
    /// Aiken CBOR round-trip for primitive Plutus data vectors.
    /// </summary>
    [Theory]
    [InlineData("D87980")]
    [InlineData("D87A80")]
    [InlineData("D8799F0304FF")]
    [InlineData("D8799F1A000F424007FF")]
    public void AikenCborRoundTrip(string aikenHex)
    {
        byte[] original = Convert.FromHexString(aikenHex);
        IPlutusData val = CborSerializer.Deserialize<IPlutusData>(original);
        Assert.Equal(aikenHex, Hex(CborSerializer.Serialize(val)));
    }
}
