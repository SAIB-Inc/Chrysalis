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
