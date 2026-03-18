using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Tx.Extensions;
using Chrysalis.Wallet.Models.Enums;
using WizardProtocol.P2p.Blueprint;
using Xunit;

namespace Chrysalis.Codec.Blueprint.Test;

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

    [Fact]
    public void ScriptPropertyReturnsValidPlutusV3Script()
    {
        IScript script = WizardScriptSpend.Script;
        Assert.NotNull(script);
        Assert.IsType<PlutusV3Script>(script);
    }

    [Fact]
    public void ScriptHashMatchesConstant()
    {
        IScript script = WizardScriptSpend.Script;
        Assert.Equal(WizardScriptSpend.Hash, script.HashHex(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TestnetAddressIsBech32()
    {
        Chrysalis.Wallet.Models.Addresses.Address addr = WizardScriptSpend.TestnetAddress;
        string bech32 = addr.ToBech32();
        Assert.StartsWith("addr_test1", bech32, StringComparison.Ordinal);
    }

    [Fact]
    public void MainnetAddressIsBech32()
    {
        Chrysalis.Wallet.Models.Addresses.Address addr = WizardScriptSpend.MainnetAddress;
        string bech32 = addr.ToBech32();
        Assert.StartsWith("addr1", bech32, StringComparison.Ordinal);
    }

    [Fact]
    public void GetAddressWithStakeKeyProducesDelegationAddress()
    {
        byte[] stakeKeyHash = new byte[28];
        Chrysalis.Wallet.Models.Addresses.Address addr = WizardScriptSpend.GetAddress(NetworkType.Testnet, stakeKeyHash);
        string bech32 = addr.ToBech32();
        Assert.StartsWith("addr_test1", bech32, StringComparison.Ordinal);
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
        Sundae.Contracts.Blueprint.PoolDatum datum = new(
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
        Sundae.Contracts.Blueprint.IOrder strategy = new Sundae.Contracts.Blueprint.Strategy(Auth: default!);
        Sundae.Contracts.Blueprint.IOrder swap = new Sundae.Contracts.Blueprint.Swap(Offer: default!, MinReceived: default!);
        Sundae.Contracts.Blueprint.IOrder deposit = new Sundae.Contracts.Blueprint.Deposit(Assets: default!);
        Sundae.Contracts.Blueprint.IOrder withdrawal = new Sundae.Contracts.Blueprint.Withdrawal(Amount: default!);
        Sundae.Contracts.Blueprint.IOrder donation = new Sundae.Contracts.Blueprint.Donation(Assets: default!);
        Sundae.Contracts.Blueprint.IOrder record = new Sundae.Contracts.Blueprint.Record(Policy: default!);

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
        Sundae.Contracts.Blueprint.ISundaeMultisigMultisigScript sig = new Sundae.Contracts.Blueprint.SundaeMultisigMultisigScriptSignature(KeyHash: default!);
        Sundae.Contracts.Blueprint.ISundaeMultisigMultisigScript allOf = new Sundae.Contracts.Blueprint.AllOf(Scripts: default!);

        Assert.NotNull(sig);
        Assert.NotNull(allOf);
    }

    /// <summary>
    /// Verifies SettingsDatum has 12 fields including nested option and list types.
    /// </summary>
    [Fact]
    public void SettingsDatumHasTwelveFields()
    {
        Sundae.Contracts.Blueprint.SettingsDatum datum = new(
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
        Assert.False(string.IsNullOrEmpty(Sundae.Contracts.Blueprint.PoolSpend.CompiledCode));
        Assert.False(string.IsNullOrEmpty(Sundae.Contracts.Blueprint.OrderSpend.Hash));
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
        Aiken.AmmDexV2.Blueprint.PoolDatum datum = new(
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
    public void MinswapValidatorsAreGenerated() => Assert.False(string.IsNullOrEmpty(Aiken.AmmDexV2.Blueprint.PoolValidatorValidatePool.CompiledCode));
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
        Merkle.Merkle.Blueprint.IAikenMerklePatriciaForestryProofStep branch =
            new Merkle.Merkle.Blueprint.Branch(Skip: default!, Neighbors: default!);
        Merkle.Merkle.Blueprint.IAikenMerklePatriciaForestryProofStep fork =
            new Merkle.Merkle.Blueprint.Fork(Skip: default!, Neighbor: default!);
        Merkle.Merkle.Blueprint.IAikenMerklePatriciaForestryProofStep leaf =
            new Merkle.Merkle.Blueprint.Leaf(Skip: default!, Key: default!, Value: default!);

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
