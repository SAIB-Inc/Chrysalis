using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using WizardProtocolp2p.Blueprint;
using Xunit;

namespace Chrysalis.Blueprint.Test;

/// <summary>
/// Verifies that the blueprint source generator produces correct types.
/// </summary>
public class BlueprintGenerationTests
{
    /// <summary>
    /// Verifies WizardDatum is generated with the correct field types.
    /// </summary>
    [Fact]
    public void WizardDatumHasCorrectFields()
    {
        WizardDatum datum = new(
            Kind: new AutoLimit(),
            AssetPair: default!,
            SwapPrice: default!,
            MinimumPrice: null,
            Owner: default!
        );

        Assert.NotNull(datum);
    }

    /// <summary>
    /// Verifies OrderKind is generated as a union with two variants.
    /// </summary>
    [Fact]
    public void OrderKindIsUnionWithVariants()
    {
        IOrderKind autoLimit = new AutoLimit();
        IOrderKind fixedPrice = new FixedPrice();

        Assert.NotNull(autoLimit);
        Assert.NotNull(fixedPrice);
    }

    /// <summary>
    /// Verifies validator constants are generated from the blueprint.
    /// </summary>
    [Fact]
    public void ValidatorConstantsAreGenerated()
    {
        Assert.False(string.IsNullOrEmpty(WizardScriptSpend.CompiledCode));
        Assert.False(string.IsNullOrEmpty(WizardScriptSpend.Hash));
        Assert.Equal("v3", WizardScriptSpend.PlutusVersion);
    }
}

/// <summary>
/// Tests for SundaeSwap contracts blueprint (48 definitions, nested tuples, recursive MultisigScript).
/// </summary>
public class SundaeBlueprintTests
{
    /// <summary>
    /// Verifies PoolDatum has 8 fields with correct types including nested tuple.
    /// </summary>
    [Fact]
    public void PoolDatumHasCorrectFields()
    {
        Sundaecontracts.Blueprint.PoolDatum datum = new(
            Identifier: default!,
            Assets: default!,
            CirculatingLp: default!,
            BidFeesPer10Thousand: default!,
            AskFeesPer10Thousand: default!,
            FeeManager: null,
            MarketOpen: default!,
            ProtocolFees: default!
        );

        Assert.NotNull(datum);
    }

    /// <summary>
    /// Verifies Order union has 6 variants (Strategy, Swap, Deposit, Withdrawal, Donation, Record).
    /// </summary>
    [Fact]
    public void OrderUnionHasSixVariants()
    {
        Sundaecontracts.Blueprint.IOrder strategy = new Sundaecontracts.Blueprint.Strategy(Auth: default!);
        Sundaecontracts.Blueprint.IOrder swap = new Sundaecontracts.Blueprint.Swap(Offer: default!, MinReceived: default!);
        Sundaecontracts.Blueprint.IOrder deposit = new Sundaecontracts.Blueprint.Deposit(Assets: default!);
        Sundaecontracts.Blueprint.IOrder withdrawal = new Sundaecontracts.Blueprint.Withdrawal(Amount: default!);
        Sundaecontracts.Blueprint.IOrder donation = new Sundaecontracts.Blueprint.Donation(Assets: default!);
        Sundaecontracts.Blueprint.IOrder record = new Sundaecontracts.Blueprint.Record(Policy: default!);

        Assert.NotNull(strategy);
        Assert.NotNull(swap);
        Assert.NotNull(deposit);
        Assert.NotNull(withdrawal);
        Assert.NotNull(donation);
        Assert.NotNull(record);
    }

    /// <summary>
    /// Verifies MultisigScript recursive union and list wrapper are generated correctly.
    /// </summary>
    [Fact]
    public void MultisigScriptRecursiveUnionIsGenerated()
    {
        Sundaecontracts.Blueprint.ISundaeMultisigMultisigScript sig = new Sundaecontracts.Blueprint.SundaeMultisigMultisigScriptSignature(KeyHash: default!);
        Sundaecontracts.Blueprint.ISundaeMultisigMultisigScript allOf = new Sundaecontracts.Blueprint.AllOf(Scripts: default!);

        Assert.NotNull(sig);
        Assert.NotNull(allOf);
    }

    /// <summary>
    /// Verifies SettingsDatum has 12 fields including nested option and list types.
    /// </summary>
    [Fact]
    public void SettingsDatumHasTwelveFields()
    {
        Sundaecontracts.Blueprint.SettingsDatum datum = new(
            SettingsAdmin: default!,
            MetadataAdmin: default!,
            TreasuryAdmin: default!,
            TreasuryAddress: default!,
            TreasuryAllowance: default!,
            AuthorizedScoopers: null,
            AuthorizedStakingKeys: default!,
            BaseFee: default!,
            SimpleFee: default!,
            StrategyFee: default!,
            PoolCreationFee: default!,
            Extensions: default!
        );

        Assert.NotNull(datum);
    }

    /// <summary>
    /// Verifies sundae validators are generated.
    /// </summary>
    [Fact]
    public void SundaeValidatorsAreGenerated()
    {
        Assert.False(string.IsNullOrEmpty(Sundaecontracts.Blueprint.PoolSpend.CompiledCode));
        Assert.False(string.IsNullOrEmpty(Sundaecontracts.Blueprint.OrderSpend.Hash));
    }
}

/// <summary>
/// Tests for Minswap DEX V2 blueprint (10 validators, nested generics).
/// </summary>
public class MinswapBlueprintTests
{
    /// <summary>
    /// Verifies PoolDatum has 10 fields.
    /// </summary>
    [Fact]
    public void PoolDatumHasCorrectFields()
    {
        AikenammDexV2.Blueprint.PoolDatum datum = new(
            PoolBatchingStakeCredential: default!,
            AssetA: default!,
            AssetB: default!,
            TotalLiquidity: default!,
            ReserveA: default!,
            ReserveB: default!,
            BaseFeeANumerator: default!,
            BaseFeeBNumerator: default!,
            FeeSharingNumeratorOpt: null,
            AllowDynamicFee: default!
        );

        Assert.NotNull(datum);
    }

    /// <summary>
    /// Verifies Minswap validators are generated.
    /// </summary>
    [Fact]
    public void MinswapValidatorsAreGenerated() => Assert.False(string.IsNullOrEmpty(AikenammDexV2.Blueprint.PoolValidatorValidatePool.CompiledCode));
}

/// <summary>
/// Tests for Merkle Patricia Forestry blueprint (Merkle proofs, Bool type).
/// </summary>
public class MerkleBlueprintTests
{
    /// <summary>
    /// Verifies ProofStep union has 3 variants (Branch, Fork, Leaf).
    /// </summary>
    [Fact]
    public void ProofStepUnionHasThreeVariants()
    {
        Merklemerkle.Blueprint.IAikenMerklePatriciaForestryProofStep branch =
            new Merklemerkle.Blueprint.Branch(Skip: default!, Neighbors: default!);
        Merklemerkle.Blueprint.IAikenMerklePatriciaForestryProofStep fork =
            new Merklemerkle.Blueprint.Fork(Skip: default!, Neighbor: default!);
        Merklemerkle.Blueprint.IAikenMerklePatriciaForestryProofStep leaf =
            new Merklemerkle.Blueprint.Leaf(Skip: default!, Key: default!, Value: default!);

        Assert.NotNull(branch);
        Assert.NotNull(fork);
        Assert.NotNull(leaf);
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
        CrashrIomarketplace.Blueprint.ICredential vk = new CrashrIomarketplace.Blueprint.VerificationKeyCredential(Field0: default!);
        CrashrIomarketplace.Blueprint.Address addr = new(PaymentCredential: vk, StakeCredential: null);

        Assert.NotNull(addr);
    }

    /// <summary>
    /// Verifies crashr validator is generated.
    /// </summary>
    [Fact]
    public void CrashrValidatorIsGenerated() => Assert.False(string.IsNullOrEmpty(CrashrIomarketplace.Blueprint.AskSpend.CompiledCode));
}

/// <summary>
/// CBOR serialization tests verifying Chrysalis round-trips Aiken-produced CBOR identically.
/// Hex vectors generated by an Aiken project with matching type definitions (cbor.serialise).
/// Uses IPlutusData for deserialization since blueprint-generated types lack codec methods
/// (source generators cannot see each other's output in the same compilation).
/// </summary>
public class CborSerializationTests
{
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToUpperInvariant();

    /// <summary>
    /// Aiken: VerificationKeyCredential(#"aabbccdd...") → constr 0, 1 field (28 bytes).
    /// </summary>
    [Fact]
    public void VerificationKeyCredentialRoundTrip()
    {
        const string aikenHex = "D8799F581CAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDFF";
        byte[] original = Convert.FromHexString(aikenHex);
        PlutusConstr val = Assert.IsType<PlutusConstr>(CborSerializer.Deserialize<IPlutusData>(original));
        Assert.Equal(121, val.ConstrIndex);
        Assert.Equal(aikenHex, Hex(CborSerializer.Serialize(val)));
    }

    /// <summary>
    /// Aiken: ScriptCredential(#"11223344...") → constr 1, 1 field (28 bytes).
    /// </summary>
    [Fact]
    public void ScriptCredentialRoundTrip()
    {
        const string aikenHex = "D87A9F581C11223344112233441122334411223344112233441122334411223344FF";
        byte[] original = Convert.FromHexString(aikenHex);
        PlutusConstr val = Assert.IsType<PlutusConstr>(CborSerializer.Deserialize<IPlutusData>(original));
        Assert.Equal(122, val.ConstrIndex);
        Assert.Equal(aikenHex, Hex(CborSerializer.Serialize(val)));
    }

    /// <summary>
    /// Aiken: Address { VK(..), stake_credential: None } → constr 0, nested constrs.
    /// </summary>
    [Fact]
    public void AddressNoStakeRoundTrip()
    {
        const string aikenHex = "D8799FD8799F581CAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDFFD87A80FF";
        byte[] original = Convert.FromHexString(aikenHex);
        IPlutusData val = CborSerializer.Deserialize<IPlutusData>(original);
        Assert.Equal(aikenHex, Hex(CborSerializer.Serialize(val)));
    }

    /// <summary>
    /// Aiken: Address { VK(..), stake_credential: Some(Script(..)) } → nested option.
    /// </summary>
    [Fact]
    public void AddressWithStakeRoundTrip()
    {
        const string aikenHex = "D8799FD8799F581CAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDFFD8799FD87A9F581C11223344112233441122334411223344112233441122334411223344FFFFFF";
        byte[] original = Convert.FromHexString(aikenHex);
        IPlutusData val = CborSerializer.Deserialize<IPlutusData>(original);
        Assert.Equal(aikenHex, Hex(CborSerializer.Serialize(val)));
    }

    /// <summary>
    /// Aiken: SimpleDatum { Address{VK,None}, 1000000, #"cafe", True } → full datum.
    /// </summary>
    [Fact]
    public void SimpleDatumRoundTrip()
    {
        const string aikenHex = "D8799FD8799FD8799F581CAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDFFD87A80FF1A000F424042CAFED87A80FF";
        byte[] original = Convert.FromHexString(aikenHex);
        IPlutusData val = CborSerializer.Deserialize<IPlutusData>(original);
        Assert.Equal(aikenHex, Hex(CborSerializer.Serialize(val)));
    }

    /// <summary>
    /// Aiken: SimpleDatum { Address{Script,Some(VK)}, 0, #"", False } → complex nested datum.
    /// </summary>
    [Fact]
    public void SimpleDatumInactiveRoundTrip()
    {
        const string aikenHex = "D8799FD8799FD87A9F581C11223344112233441122334411223344112233441122334411223344FFD8799FD8799F581CAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDFFFFFF0040D87980FF";
        byte[] original = Convert.FromHexString(aikenHex);
        IPlutusData val = CborSerializer.Deserialize<IPlutusData>(original);
        Assert.Equal(aikenHex, Hex(CborSerializer.Serialize(val)));
    }

    /// <summary>
    /// Aiken CBOR round-trip for primitive Plutus data vectors.
    /// D87980 = AutoLimit/False (constr 0, empty), D87A80 = FixedPrice/True/None (constr 1, empty),
    /// D8799F0304FF = Rational{3,4}, D8799F1A000F424007FF = Rational{1000000,7}.
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
