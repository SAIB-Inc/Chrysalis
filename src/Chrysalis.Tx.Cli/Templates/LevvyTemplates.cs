using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Cli.Templates.Models;
using Chrysalis.Tx.Cli.Templates.Parameters;
using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Models.Addresses;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;
using LevvyOutputIndices = Chrysalis.Tx.Cli.Templates.Models.OutputIndices;
using LevvyAction = Chrysalis.Tx.Cli.Templates.Models.Action;

namespace Chrysalis.Tx.Cli.Templates;
public class LevvyTemplates
{
    private readonly ICardanoDataProvider provider;
    private readonly Dictionary<string, Func<LockProtocolParamsParameters, Task<Transaction>>> templates;
    private readonly string ChangeAddress;

    public LevvyTemplates(ICardanoDataProvider provider, string bech32ChangeAddress)
    {
        this.provider = provider;
        templates = [];
        ChangeAddress = bech32ChangeAddress;

        InitializeTemplates();
    }

    private void InitializeTemplates()
    {
        string pparamsScriptRef = "ed1b106b38497a435add07d99ebaa1aabb2d481b7ab4f1f93a15e0a43c72f702";

        var lockPparams = TransactionTemplateBuilder<LockProtocolParamsParameters>.Create(provider)
            .AddStaticParty("change", ChangeAddress, true)
            .AddInput((options, parameters) =>
            {
                options.From = "change";
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "validator";
                options.Amount = new LovelaceWithMultiAsset(
                    new Lovelace(10000000UL),
                    new MultiAssetOutput(
                        new Dictionary<byte[], TokenBundleOutput>
                        {
                            { Convert.FromHexString(parameters.Policy), new TokenBundleOutput(new Dictionary<byte[], ulong>(){
                                { Convert.FromHexString(parameters.AssetName), 1 }
                            }) }
                        }
                    )
                );
                options.Datum = new InlineDatumOption(1, new CborEncodedValue(CborSerializer.Serialize(CborSerializer.Deserialize<PlutusData>(CborSerializer.Serialize(parameters.GlobalParams!)))));
            })
            .Build();

    }

    public Func<LendParams, Task<Transaction>> MultiSigLend()
    {
        var multiSigLend = TransactionTemplateBuilder<LendParams>.Create(provider)
            .AddStaticParty("change", ChangeAddress, true)
            .AddInput((options, parameters) =>
            {
                options.From = "change";
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "validator";
                options.Amount = new Lovelace(parameters.LendDatum.LendDetails.PrincipalDetails.Amount);
                options.Datum = new InlineDatumOption(1, new CborEncodedValue(CborSerializer.Serialize(CborSerializer.Deserialize<PlutusData>(CborSerializer.Serialize(parameters.LendDatum!)))));
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "validator";
                options.Amount = new Lovelace(parameters.LendDatum.LendDetails.PrincipalDetails.Amount);
                options.Datum = new InlineDatumOption(1, new CborEncodedValue(CborSerializer.Serialize(CborSerializer.Deserialize<PlutusData>(CborSerializer.Serialize(parameters.LendDatum!)))));
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "validator";
                options.Amount = new Lovelace(parameters.LendDatum.LendDetails.PrincipalDetails.Amount);
                options.Datum = new InlineDatumOption(1, new CborEncodedValue(CborSerializer.Serialize(CborSerializer.Deserialize<PlutusData>(CborSerializer.Serialize(parameters.LendDatum!)))));
            })
            .Build();

        return multiSigLend;
    }

    public Func<CancelParams, Task<Transaction>> Cancel()
    {
        string mainValidatorAddress = "addr_test1wra8f56lfvx53trz3zk9e6n3728gqat945ycg53j7g5kvrc5qqfl0";
        string mainValidatorScriptRef = "5d84910a2e0ece53b64fe2bf0f0d3cdc8f32993d3a5b3fee7c15a8e237fc9e16";

        string cancelValidatorAddress = "addr_test1wzgvnetp806a7ff5v3cerwqk5pcwe7axax7uc424mpzm4ns08tkm5";
        string cancelValidatorScriptRef = "4af67781d38bbbc481538810889bd97c5888d9a19aa7122d27e435204003f4d7";
        string cancelValidatorRewardAddress = "stake_test17zgvnetp806a7ff5v3cerwqk5pcwe7axax7uc424mpzm4ns004wv7";

        RedeemerDataBuilder<CancelParams, LevvyAction> cancelRedeemerBuilder1 = (mapping, parameters) =>
        {
            var (InputIndex, OutputIndexes) = mapping.GetInput("lockedUtxo1");
            InputIndices inputIndices = new((int)InputIndex, new None<int>());
            LevvyOutputIndices outputIndices = new((int)OutputIndexes["cancelOutput1"], new None<int>(), new None<int>(), new None<int>());

            ActionParams actionParams = new(inputIndices, new None<int>(), new None<int>(), outputIndices, new Token());

            return new CancelAction(actionParams);
        };
        RedeemerDataBuilder<CancelParams, LevvyAction> cancelRedeemerBuilder2 = (mapping, parameters) =>
        {
            var (InputIndex, OutputIndexes) = mapping.GetInput("lockedUtxo2");
            InputIndices inputIndices = new((int)InputIndex, new None<int>());
            LevvyOutputIndices outputIndices = new((int)OutputIndexes["cancelOutput2"], new None<int>(), new None<int>(), new None<int>());

            ActionParams actionParams = new(inputIndices, new None<int>(), new None<int>(), outputIndices, new Token());

            return new CancelAction(actionParams);
        };
        RedeemerDataBuilder<CancelParams, LevvyAction> cancelRedeemerBuilder3 = (mapping, parameters) =>
        {
            var (InputIndex, OutputIndexes) = mapping.GetInput("lockedUtxo3");
            InputIndices inputIndices = new((int)InputIndex, new None<int>());
            LevvyOutputIndices outputIndices = new((int)OutputIndexes["cancelOutput3"], new None<int>(), new None<int>(), new None<int>());

            ActionParams actionParams = new(inputIndices, new None<int>(), new None<int>(), outputIndices, new Token());

            return new CancelAction(actionParams);
        };

        RedeemerDataBuilder<CancelParams, CborIndefList<Outref>> withdrawRedeemer = (mapping, parameters) =>
        {
            Outref outref1 = new(Convert.FromHexString("3e3e8e674f502cdca4f05854508e16f2b09c0a88400421bcf638a130d010a8dc"), 0);
            Outref outref2 = new(Convert.FromHexString("3e3e8e674f502cdca4f05854508e16f2b09c0a88400421bcf638a130d010a8dc"), 1);
            Outref outref3 = new(Convert.FromHexString("3e3e8e674f502cdca4f05854508e16f2b09c0a88400421bcf638a130d010a8dc"), 2);
            CborIndefList<Outref> outrefs = new([outref1, outref2, outref3]);

            return outrefs;
        };


        var cancel = TransactionTemplateBuilder<CancelParams>.Create(provider)
            .AddStaticParty("change", ChangeAddress, true)
            .AddStaticParty("mainValidator", mainValidatorAddress)
            .AddStaticParty("cancelValidator", cancelValidatorAddress)
            .AddStaticParty("cancelValidatorRewardAddress", cancelValidatorRewardAddress)
            .AddReferenceInput((options, parameters) =>
            {
                options.From = "mainValidator";
                options.UtxoRef = new TransactionInput(
                   Convert.FromHexString(mainValidatorScriptRef), 0
                );
                options.Id = "mainValidator";
            })
            .AddReferenceInput((options, parameters) =>
            {
                options.From = "cancelValidator";
                options.UtxoRef = new TransactionInput(
                   Convert.FromHexString(cancelValidatorScriptRef), 0
                );
                options.Id = "cancelValidator";
            })
            .AddInput((options, parameters) =>
            {
                options.From = "change";
                options.Id = "changeAddress";
            })
            .AddInput((options, parameters) =>
            {
                options.From = "mainValidator";
                options.UtxoRef = parameters.lockedUtxos[0];
                options.Id = "lockedUtxo1";
                options.SetRedeemerBuilder(cancelRedeemerBuilder1);
            })
            .AddInput((options, parameters) =>
            {
                options.From = "mainValidator";
                options.UtxoRef = parameters.lockedUtxos[1];
                options.Id = "lockedUtxo2";
                options.SetRedeemerBuilder(cancelRedeemerBuilder2);
            })
            .AddInput((options, parameters) =>
            {
                options.From = "mainValidator";
                options.UtxoRef = parameters.lockedUtxos[2];
                options.Id = "lockedUtxo3";
                options.SetRedeemerBuilder(cancelRedeemerBuilder3);
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "change";
                options.Amount = parameters.principalAmount;
                options.AssociatedInputId = "lockedUtxo1";
                options.Id = "cancelOutput1";
            })
             .AddOutput((options, parameters) =>
            {
                options.To = "change";
                options.Amount = parameters.principalAmount;
                options.AssociatedInputId = "lockedUtxo2";
                options.Id = "cancelOutput2";
            })
             .AddOutput((options, parameters) =>
            {
                options.To = "change";
                options.Amount = parameters.principalAmount;
                options.AssociatedInputId = "lockedUtxo3";
                options.Id = "cancelOutput3";
            })
            .AddRequiredSigner("change")
            .AddWithdrawal((options, parameters) =>
            {
                options.From = "cancelValidatorRewardAddress";
                options.Amount = 0;
                options.SetRedeemerFactory(withdrawRedeemer);
            })
            .Build();

        return cancel;
    }



    public Func<LockProtocolParamsParameters, Task<Transaction>> LockPparams => templates["lock"];

}