using Chrysalis.Codec.Extensions.Cardano.Core.Common;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.VM.Models;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Fees;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Utils;
using SAIB.Cbor.Serialization;
using Xunit;

namespace Chrysalis.Tx.Test;

public class Tier2aTxMapTests
{
    private static readonly string RewardA = "E0" + new string('A', 56);
    private static readonly string RewardB = "E0" + new string('B', 56);
    private static readonly byte[] AddrBytes = [0x60, .. Enumerable.Repeat((byte)0x11, 28)];
    private static readonly byte[] TxHash = [.. Enumerable.Repeat((byte)0x22, 32)];

    private static TransactionBuilder BaseBuilder() => new TransactionBuilder()
        .AddInput(TransactionInput.Create(TxHash, 0))
        .AddOutput(AlonzoTransactionOutput.Create(Address.Create(AddrBytes), Lovelace.Create(2_000_000), null))
        .SetFee(200_000);

    private static ProtocolParams TestParams() => new(
        null, null, null, null, null, null, null, null, null, null, null, null, null, null,
        AdaPerUTxOByte: 4310,
        CostModelsForScriptLanguage: null,
        ExecutionCosts: ExUnitPrices.Create(CborRationalNumber.Create(1, 1), CborRationalNumber.Create(1, 1)),
        MaxTxExUnits: null, MaxBlockExUnits: null, MaxValueSize: null,
        CollateralPercentage: 150, MaxCollateralInputs: 3,
        PoolVotingThresholds: null, DRepVotingThresholds: null,
        MinCommitteeSize: null, CommitteeTermLimit: null, GovernanceActionValidityPeriod: null,
        GovernanceActionDeposit: null, DRepDeposit: null, DRepInactivityPeriod: null,
        MinFeeRefScriptCostPerByte: null);

    [Fact]
    public void TransactionBuilderWithdrawalMergeIdempotenceAndSingleWireCountOnDuplicateAdd()
    {
        // SetWithdrawals + AddWithdrawal merge + BuildBody idempotence
        Withdrawals initial = Withdrawals.Create(CborMap<RewardAccount, ulong>.Create(
            [new(new RewardAccount(Convert.FromHexString(RewardA)), 1_000_000)]));
        TransactionBuilder merged = BaseBuilder()
            .SetWithdrawals(initial)
            .AddWithdrawal(RewardB, 3_000_000)
            .AddWithdrawal(RewardA, 4_000_000);

        ConwayTransactionBody body1 = merged.BuildBody();
        ConwayTransactionBody body2 = merged.BuildBody();
        Assert.Equal(CborSerializer.Serialize(body1), CborSerializer.Serialize(body2));
        Withdrawals roundTripped = CborSerializer.Deserialize<Withdrawals>(CborSerializer.Serialize(body1.Withdrawals!.Value));
        Assert.Equal(2, roundTripped.Value.Count);
        Assert.Equal(4_000_000UL, roundTripped.Value[new RewardAccount(Convert.FromHexString(RewardA))]);
        Assert.Equal(3_000_000UL, roundTripped.Value[new RewardAccount(Convert.FromHexString(RewardB))]);

        // No-CurrentWithdrawals path: calling AddWithdrawal twice for the same reward account writes wire count 1 (not 2!).
        TransactionBuilder dupAdd = BaseBuilder()
            .AddWithdrawal(RewardA, 1_000_000)
            .AddWithdrawal(RewardA, 2_000_000);
        ConwayTransactionBody dupBody = Assert.IsType<ConwayTransactionBody>(dupAdd.Build().Body);
        byte[] wire = CborSerializer.Serialize(dupBody.Withdrawals!.Value);
        CborReader reader = new(wire);
        reader.ReadBeginMap();
        Assert.Equal(1, reader.ReadSize());
        Withdrawals reread = CborSerializer.Deserialize<Withdrawals>(wire);
        Assert.Equal(2_000_000UL, reread.Value[new RewardAccount(Convert.FromHexString(RewardA))]);
    }

    [Fact]
    public void PublicConsumersKeepLastValueOnceOnDuplicateWireMaps()
    {
        // 1. FeeEngine.ScriptFee on duplicate RedeemerMap prices ONLY last ExUnits.
        RedeemerMap dupRedeemers = RedeemerMap.Read(Convert.FromHexString("A2820000820182186418648200008201820A14"));
        ProtocolParams pp = TestParams();
        Assert.Equal(30UL, FeeEngine.ScriptFee(dupRedeemers, pp));

        // 2. ScriptContextBuilder.BuildTxInfoV3 sees 1 entry each for duplicate mint, withdrawal, and redeemer.
        string policyHex = new('C', 56);
        byte[] bodyWire = Convert.FromHexString(
            $"A50081825820{Convert.ToHexString(TxHash)}000180021A00030D40" +
            $"05A2581D{RewardA}01581D{RewardA}05" +
            $"09A2581C{policyHex}A1416101581C{policyHex}A1416107");
        ConwayTransactionBody body = ConwayTransactionBody.Read(bodyWire);
        PostAlonzoTransactionWitnessSet witnesses = PostAlonzoTransactionWitnessSet.Read(
            Convert.FromHexString("A105A2820000820182186418648200008202820A14"));
        ResolvedInput utxo = new(
            TransactionInput.Create(TxHash, 0),
            AlonzoTransactionOutput.Create(Address.Create(AddrBytes), Lovelace.Create(5_000_000), null));
        PlutusDataConstr txInfo = Assert.IsType<PlutusDataConstr>(
            ScriptContextBuilder.BuildTxInfoV3(body, witnesses, [utxo], null));
        Assert.Single(Assert.IsType<PlutusDataMap>(txInfo.Fields[4]).Entries);
        Assert.Single(Assert.IsType<PlutusDataMap>(txInfo.Fields[6]).Entries);
        Assert.Single(Assert.IsType<PlutusDataMap>(txInfo.Fields[9]).Entries);

        // 3. TransactionBuilderExtensions: duplicate withdrawal balance counts last-wins (2, not 3).
        Withdrawals dupWd = Withdrawals.Read(Convert.FromHexString($"A2581D{RewardA}01581D{RewardA}02"));
        TransactionBuilder balBuilder = TransactionBuilder.Create(pp)
            .SetWithdrawals(dupWd)
            .AddInput(utxo.Outref)
            .SetFee(0);
        string changeAddr = Chrysalis.Wallet.Models.Addresses.Address.FromBytes(AddrBytes).ToBech32();
        (ulong budget, ulong deficit) = TransactionBuilderExtensions.BuildChangeOutput(balBuilder, [utxo], changeAddr, includeCertificateCoins: false);
        Assert.Equal(5_000_002UL, budget);
        Assert.Equal(0UL, deficit);

        // 4. TransactionBuilderExtensions: collateral return AggregateAssets on duplicate nested multi-asset counts last-wins once.
        IValue dupMultiVal = LovelaceWithMultiAsset.Read(
            Convert.FromHexString($"821A004C4B40A2581C{policyHex}A1416101581C{policyHex}A2416102416105"));
        ResolvedInput colUtxo = new(
            TransactionInput.Create(TxHash, 1),
            AlonzoTransactionOutput.Create(Address.Create(AddrBytes), dupMultiVal, null));
        TransactionBuilder colBuilder = TransactionBuilder.Create(pp);
        Assert.True(TransactionBuilderExtensions.TrySelectCollateral(colBuilder, 100_000, [colUtxo]));
        ITransactionOutput colRet = Assert.IsAssignableFrom<ITransactionOutput>(colBuilder.CollateralReturn);
        Assert.Equal(5UL, colRet.Amount().QuantityOf(Convert.FromHexString(policyHex), "a"u8.ToArray()));

        // 5. TransactionBuilderExtensions.UpdateRedeemersWithEvalResults on duplicate redeemer keeps 1 entry and does not throw.
        TransactionBuilder evalBuilder = new TransactionBuilder().SetRedeemers(dupRedeemers);
        EvaluationResult[] evals = [new(0, 0, new(42, 84))];
        Assert.True(TransactionBuilderExtensions.UpdateRedeemersWithEvalResults(evalBuilder, evals));
        RedeemerMap updated = Assert.IsType<RedeemerMap>(evalBuilder.Redeemers);
        Assert.Equal(1, updated.Value.Count);
        Assert.Equal(42UL, updated.Value[RedeemerKey.Create(0, 0)].ExUnits.Mem);

        // 6. CoinSelectionUtil on duplicate multi-asset output counts last-wins once.
        IValue reqVal = LovelaceWithMultiAsset.Create(2_000_000, MultiAssetOutput.Create(
            CborMap<ReadOnlyMemory<byte>, TokenBundleOutput>.Create(
                [new(Convert.FromHexString(policyHex), TokenBundleOutput.Create(
                    CborMap<ReadOnlyMemory<byte>, ulong>.Create([new("a"u8.ToArray(), 2UL)])))])));
        CoinSelectionResult selected = CoinSelectionUtil.Select([colUtxo], [reqVal]);
        Assert.Equal(3UL, selected.AssetsChange[Convert.FromHexString(policyHex)].Value["a"u8.ToArray()]);

        // 7. MultiAssetExtension.TokenBundleByPolicyId on duplicate asset names does not throw and keeps last-wins.
        MultiAssetOutput dupOut = MultiAssetOutput.Read(Convert.FromHexString($"A1581C{policyHex}A2416102416109"));
        MultiAssetMint dupMint = MultiAssetMint.Read(Convert.FromHexString($"A1581C{policyHex}A2416102416108"));
        TokenBundleOutput bundle = dupOut.Value[Convert.FromHexString(policyHex)];
        Assert.Equal(new KeyValuePair<string, ulong>("61", 9UL), Assert.Single(bundle.ToDict()));
        Assert.Equal(9UL, dupOut.TokenBundleByPolicyId(policyHex)!["61"]);
        Assert.Equal(8UL, dupMint.TokenBundleByPolicyId(policyHex)!["61"]);
        Assert.Equal([policyHex], dupOut.PolicyId());
    }
}
