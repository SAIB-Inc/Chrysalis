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
using Chrysalis.Wallet.Utils;

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

        templates.Add("lock", lockPparams);
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
            // .AddOutput((options, parameters) =>
            // {
            //     options.To = "validator";
            //     options.Amount = new Lovelace(parameters.LendDatum.LendDetails.PrincipalDetails.Amount);
            //     options.Datum = new InlineDatumOption(1, new CborEncodedValue(CborSerializer.Serialize(CborSerializer.Deserialize<PlutusData>(CborSerializer.Serialize(parameters.LendDatum!)))));
            // })
            // .AddOutput((options, parameters) =>
            // {
            //     options.To = "validator";
            //     options.Amount = new Lovelace(parameters.LendDatum.LendDetails.PrincipalDetails.Amount);
            //     options.Datum = new InlineDatumOption(1, new CborEncodedValue(CborSerializer.Serialize(CborSerializer.Deserialize<PlutusData>(CborSerializer.Serialize(parameters.LendDatum!)))));
            // })
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
        // RedeemerDataBuilder<CancelParams, LevvyAction> cancelRedeemerBuilder2 = (mapping, parameters) =>
        // {
        //     var (InputIndex, OutputIndexes) = mapping.GetInput("lockedUtxo2");
        //     InputIndices inputIndices = new((int)InputIndex, new None<int>());
        //     LevvyOutputIndices outputIndices = new((int)OutputIndexes["cancelOutput2"], new None<int>(), new None<int>(), new None<int>());

        //     ActionParams actionParams = new(inputIndices, new None<int>(), new None<int>(), outputIndices, new Token());

        //     return new CancelAction(actionParams);
        // };
        // RedeemerDataBuilder<CancelParams, LevvyAction> cancelRedeemerBuilder3 = (mapping, parameters) =>
        // {
        //     var (InputIndex, OutputIndexes) = mapping.GetInput("lockedUtxo3");
        //     InputIndices inputIndices = new((int)InputIndex, new None<int>());
        //     LevvyOutputIndices outputIndices = new((int)OutputIndexes["cancelOutput3"], new None<int>(), new None<int>(), new None<int>());

        //     ActionParams actionParams = new(inputIndices, new None<int>(), new None<int>(), outputIndices, new Token());

        //     return new CancelAction(actionParams);
        // };

        RedeemerDataBuilder<CancelParams, CborIndefList<Outref>> withdrawRedeemer = (mapping, parameters) =>
        {
            Outref outref1 = new(Convert.FromHexString("06de79b9e917b4f075eeb15a911620ee037c46fe297de1da4176d6eef551d928"), 0);
            // Outref outref2 = new(Convert.FromHexString("3e3e8e674f502cdca4f05854508e16f2b09c0a88400421bcf638a130d010a8dc"), 1);
            // Outref outref3 = new(Convert.FromHexString("3e3e8e674f502cdca4f05854508e16f2b09c0a88400421bcf638a130d010a8dc"), 2);
            CborIndefList<Outref> outrefs = new([outref1]);

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
            // .AddInput((options, parameters) =>
            // {
            //     options.From = "mainValidator";
            //     options.UtxoRef = parameters.lockedUtxos[1];
            //     options.Id = "lockedUtxo2";
            //     options.SetRedeemerBuilder(cancelRedeemerBuilder2);
            // })
            // .AddInput((options, parameters) =>
            // {
            //     options.From = "mainValidator";
            //     options.UtxoRef = parameters.lockedUtxos[2];
            //     options.Id = "lockedUtxo3";
            //     options.SetRedeemerBuilder(cancelRedeemerBuilder3);
            // })
            .AddOutput((options, parameters) =>
            {
                options.To = "change";
                options.Amount = parameters.principalAmount;
                options.AssociatedInputId = "lockedUtxo1";
                options.Id = "cancelOutput1";
            })
            //  .AddOutput((options, parameters) =>
            // {
            //     options.To = "change";
            //     options.Amount = parameters.principalAmount;
            //     options.AssociatedInputId = "lockedUtxo2";
            //     options.Id = "cancelOutput2";
            // })
            //  .AddOutput((options, parameters) =>
            // {
            //     options.To = "change";
            //     options.Amount = parameters.principalAmount;
            //     options.AssociatedInputId = "lockedUtxo3";
            //     options.Id = "cancelOutput3";
            // })
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

    public Func<BorrowParams, Task<Transaction>> Borrow()
    {
        string mainValidatorAddress = "addr_test1wra8f56lfvx53trz3zk9e6n3728gqat945ycg53j7g5kvrc5qqfl0";
        string mainValidatorScriptRef = "5d84910a2e0ece53b64fe2bf0f0d3cdc8f32993d3a5b3fee7c15a8e237fc9e16";

        string borrowValidatorAddress = "addr_test1wq2gmfnfs3u9mrz52munwsrdmrznk79ua40kpagmv9gas7gtzfurs";
        string borrowValidatorScriptRef = "58992377ab39360724fa3f992f15597cbdbc72756aa2b2026e668d528327a001";
        string borrowValidatorRewardAddress = "stake_test17q2gmfnfs3u9mrz52munwsrdmrznk79ua40kpagmv9gas7gt2hy56";

        string protocolParamsAddress = "addr_test1wr00dqehse7tfu0etd4cz8ldhlxaw7qdzz54esrjqacg36sp45dt3";

        RedeemerDataBuilder<BorrowParams, LevvyAction> borrowRedeemerBuilder = (mapping, parameters) =>
        {
            var (InputIndex, OutputIndexes) = mapping.GetInput("lockedUtxo1");
            InputIndices inputIndices = new((int)InputIndex, new None<int>());
            LevvyOutputIndices outputIndices = new((int)OutputIndexes["borrowOutput1"], new Some<int>((int)OutputIndexes["feeOutput"]), new None<int>(), new None<int>());
            ActionParams actionParams = new(inputIndices, new Some<int>((int)mapping.GetReferenceInput("protocolParams")), new None<int>(), outputIndices, new Token());

            return new BorrowAction(actionParams);
        };

        RedeemerDataBuilder<BorrowParams, CborIndefList<Outref>> withdrawRedeemer = (mapping, parameters) =>
        {
            Outref outref1 = new(Convert.FromHexString("1a41ac32a4cf1dd9dfe2ecd812cc6851efbd1e1ee19457b9d6684e7c3ec61353"), 0);
            CborIndefList<Outref> outrefs = new([outref1]);

            return outrefs;
        };

        var borrow = TransactionTemplateBuilder<BorrowParams>.Create(provider)
            .AddStaticParty("change", ChangeAddress, true)
            .AddStaticParty("mainValidator", mainValidatorAddress)
            .AddStaticParty("borrowValidator", borrowValidatorAddress)
            .AddStaticParty("borrowValidatorRewardAddress", borrowValidatorRewardAddress)
            .AddStaticParty("protocolParamsAddress", protocolParamsAddress)
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
                options.From = "borrowValidator";
                options.UtxoRef = new TransactionInput(
                   Convert.FromHexString(borrowValidatorScriptRef), 0
                );
                options.Id = "borrowValidator";
            })
            .AddReferenceInput((options, parameters) =>
            {
                options.From = "protocolParamsAddress";
                options.UtxoRef = new TransactionInput(
                   Convert.FromHexString("1699fe1b10c18cd97f56836fff92b042d239a413c227b3786842012aa238895c"), 0
                );
                options.Id = "protocolParams";
            })
            .AddInput((options, parameters) =>
            {
                options.From = "change";
                options.Id = "changeAddress";
            })
            .AddInput((options, parameters) =>
            {
                options.From = "mainValidator";
                options.UtxoRef = parameters.LockedUtxos[0];
                options.Id = "lockedUtxo1";
                options.SetRedeemerBuilder(borrowRedeemerBuilder);
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "mainValidator";
                options.Amount = parameters.CollateralAmount;
                options.Datum = new InlineDatumOption(1, new CborEncodedValue(CborSerializer.Serialize(CborSerializer.Deserialize<PlutusData>(CborSerializer.Serialize(parameters.BorrowDatum)))));
                options.AssociatedInputId = "lockedUtxo1"; 
                options.Id = "borrowOutput1";
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "change";
                options.Amount = new Lovelace(2000000);
                options.AssociatedInputId = "lockedUtxo1";
                options.Id = "feeOutput";
                options.Datum = new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString("81D02C4C5EF5F54D324FD0EA86D242CEE83002779ACA9D0D1BF66D82EC5939AA")));
            })
            .AddWithdrawal((options, parameters) =>
            {
                options.From = "borrowValidatorRewardAddress";
                options.Amount = 0;
                options.SetRedeemerFactory(withdrawRedeemer);
            })
            .SetValidFrom(1000)
            .Build();

        return borrow;
    }

    public Func<LockProtocolParamsParameters, Task<Transaction>> LockPparams => templates["lock"];

    public Func<RepayParams, Task<Transaction>> Repay()
    {
        string mainValidatorAddress = "addr_test1wra8f56lfvx53trz3zk9e6n3728gqat945ycg53j7g5kvrc5qqfl0";
        string mainValidatorScriptRef = "5d84910a2e0ece53b64fe2bf0f0d3cdc8f32993d3a5b3fee7c15a8e237fc9e16";
    
        string repayValidatorAddress = "addr_test1wqhgytp55nvs57me5d64llpqwph2pam8d5sp8x9t64mr5qsr7y9aj";
        string repayValidatorScriptRef = "2e822c34a4d90a7b79a3755ffc20706ea0f7676d201398abd5763a02";
        string repayValidatorRewardAddress = "stake_test17qhgytp55nvs57me5d64llpqwph2pam8d5sp8x9t64mr5qsrk6a2c";

        string protocolParamsAddress = "addr_test1wr00dqehse7tfu0etd4cz8ldhlxaw7qdzz54esrjqacg36sp45dt3";

        RedeemerDataBuilder<RepayParams, LevvyAction> repayRedeemerBuilder = (mapping, parameters) =>
        {
            var (InputIndex, OutputIndexes) = mapping.GetInput("lockedUtxo1");
            InputIndices inputIndices = new((int)InputIndex, new None<int>());
            LevvyOutputIndices outputIndices = new((int)OutputIndexes["repayOutput1"], new None<int>(), new None<int>(), new None<int>());
            ActionParams actionParams = new(inputIndices, new Some<int>((int)mapping.GetReferenceInput("protocolParams")), new None<int>(), outputIndices, new Token());

            return new BorrowAction(actionParams);
        };

        RedeemerDataBuilder<RepayParams, CborIndefList<Outref>> withdrawRedeemer = (mapping, parameters) =>
        {
            Outref outref1 = new(Convert.FromHexString("25e615156cd9e10be59d9a17776ef7958d46d1f57919a5fc58a616902f0e185c"), 0);
            CborIndefList<Outref> outrefs = new([outref1]);

            return outrefs;
        };

        var repay = TransactionTemplateBuilder<RepayParams>.Create(provider)
            .AddStaticParty("change", ChangeAddress, true)
            .AddStaticParty("mainValidator", mainValidatorAddress)
            .AddStaticParty("repayValidator", repayValidatorAddress)
            .AddStaticParty("repayValidatorRewardAddress", repayValidatorRewardAddress)
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
                options.From = "repayValidator";
                options.UtxoRef = new TransactionInput(
                   Convert.FromHexString(repayValidatorScriptRef), 0
                );
                options.Id = "repayValidator";
            })
            .AddInput((options, parameters) =>
            {
                options.From = "change";
                options.Id = "changeAddress";
            })
            .AddInput((options, parameters) =>
            {
                options.From = "mainValidator";
                options.UtxoRef = parameters.LockedUtxos[0];
                options.Id = "lockedUtxo1";
                options.SetRedeemerBuilder(repayRedeemerBuilder);
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "mainValidator";
                options.Amount = new LovelaceWithMultiAsset(
                    new Lovelace(2000000),
                    new MultiAssetOutput(
                        new Dictionary<byte[], TokenBundleOutput>
                        {{
                            Convert.FromHexString(""),
                            new TokenBundleOutput(
                                new Dictionary<byte[], ulong>{{ Convert.FromHexString(""), 8000000 }}
                            )
                        }}
                    )
                );
                options.Datum = new InlineDatumOption(1, new CborEncodedValue(CborSerializer.Serialize(CborSerializer.Deserialize<PlutusData>(CborSerializer.Serialize(parameters.RepayDatum)))));
                options.AssociatedInputId = "lockedUtxo1"; 
                options.Id = "repayOutput1";
            })
            .AddWithdrawal((options, parameters) =>
            {
                options.From = "repayValidatorRewardAddress";
                options.Amount = 0;
                options.SetRedeemerFactory(withdrawRedeemer);
            })
            .Build();

            return repay;
    }
}