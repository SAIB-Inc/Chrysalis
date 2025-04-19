using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Cli.Templates.Parameters;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Cli.Templates.Models;
using Chrysalis.Tx.Cli.Templates.Parameters.NftPosition;
using LevvyAction = Chrysalis.Tx.Cli.Templates.Models.Action;
using LevvyOutputIndices = Chrysalis.Tx.Cli.Templates.Models.OutputIndices;

namespace Chrysalis.Tx.Cli.Templates;

public class LevvyNftTemplates
{
    private readonly ICardanoDataProvider provider;
    private readonly Dictionary<string, Func<LockProtocolParamsParameters, Task<Transaction>>> templates;
    private readonly string ChangeAddress;

    public LevvyNftTemplates(ICardanoDataProvider provider, string bech32ChangeAddress)
    {
        this.provider = provider;
        templates = [];
        ChangeAddress = bech32ChangeAddress;
    }

    public Func<LendMintParams, Task<Transaction>> Lend()
    {
        string mainValidatorAddress = "addr_test1wra8f56lfvx53trz3zk9e6n3728gqat945ycg53j7g5kvrc5qqfl0";
        string mainValidatorScriptRef = "5d84910a2e0ece53b64fe2bf0f0d3cdc8f32993d3a5b3fee7c15a8e237fc9e16";

        string mintValidatorAddress = "addr_test1wrmdu7kq9kf3azpley0gx5cn6sjw7gdqs2az5zj9dnwu7dcde0xmg";
        string mintValidatorScriptRef = "491f084536901de3dcae4e066a9f3ccbbb9fa3fa21786db5978202c4736b9b98";

        string protocolParamsAddress = "addr_test1wr00dqehse7tfu0etd4cz8ldhlxaw7qdzz54esrjqacg36sp45dt3";

        string withdrawalAddress = "stake_test17rmdu7kq9kf3azpley0gx5cn6sjw7gdqs2az5zj9dnwu7dcd337vz";

        RedeemerDataBuilder<LendMintParams, MintRedeemer> mintRedeemerBuilder = (mapping, parameters) =>
        {
            byte[] policyId = Convert.FromHexString(parameters.MintPolicy ?? string.Empty);
            var mappings = mapping.GetMappings();

            return new MintRedeemer(policyId, new Some<int>((int)mapping.GetReferenceInput("protocolParams")), new Some<MintOutputIndices>(new([0])));
        };

        RedeemerDataBuilder<LendMintParams, PolicyId> withdrawRedeemer = (mapping, parameters) => new PolicyId(Convert.FromHexString(parameters.MintPolicy));

        var multiSigLend = TransactionTemplateBuilder<LendMintParams>.Create(provider)
            .AddStaticParty("change", ChangeAddress, true)
            .AddStaticParty("mainValidator", mainValidatorAddress)
            .AddStaticParty("mintValidator", mintValidatorAddress)
            .AddStaticParty("protocolParams", protocolParamsAddress)
            .AddStaticParty("withdrawal", withdrawalAddress)
            .AddReferenceInput((options, parameters) =>
            {
                options.From = "protocolParams";
                options.UtxoRef = new(
                    Convert.FromHexString("faaabf3c40ffda907a9598bdb0aa27142397f63cba4874223ad1392b313fa952"),
                    0
                );
                options.Id = "protocolParams";
            })
            .AddReferenceInput((options, parameters) =>
            {
                options.From = "mintValidator";
                options.UtxoRef = new(
                    Convert.FromHexString(mintValidatorScriptRef),
                    0
                );
            })
            .AddReferenceInput((options, parameters) =>
            {
                options.From = "mainValidator";
                options.UtxoRef = new(
                    Convert.FromHexString(mainValidatorScriptRef),
                    0
                );
            })
            .AddInput((options, parameters) =>
            {
                options.From = "change";
            })
            .AddMint((options, parameters) =>
            {
                options.Policy = parameters.MintPolicy ?? string.Empty;
                options.Assets = new Dictionary<string, int>
                {
                    { parameters.UserAssetName ?? string.Empty, 1 },
                    { parameters.ReferenceAssetName ?? string.Empty, 1 }
                };
                options.SetRedeemerBuilder(mintRedeemerBuilder);
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "mainValidator";
                options.Amount = new LovelaceWithMultiAsset(
                    new Lovelace(parameters.PrincipalDetails.Amount),
                    new MultiAssetOutput(
                        new Dictionary<byte[], TokenBundleOutput>
                        {{
                            Convert.FromHexString(parameters.MintPolicy),
                            new TokenBundleOutput(new Dictionary<byte[], ulong>
                                {{ Convert.FromHexString(parameters.ReferenceAssetName ?? string.Empty), 1 }}
                            )
                        }}
                    )
                );
                options.Datum = new InlineDatumOption(1, new CborEncodedValue(CborSerializer.Serialize(CborSerializer.Deserialize<PlutusData>(CborSerializer.Serialize(parameters.NftPositionDatum)))));
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "change";
                options.Amount = new LovelaceWithMultiAsset(
                    new Lovelace(2_000_000),
                    new MultiAssetOutput(
                        new Dictionary<byte[], TokenBundleOutput>
                        {{
                            Convert.FromHexString(parameters.MintPolicy),
                            new TokenBundleOutput(new Dictionary<byte[], ulong>
                                {{ Convert.FromHexString(parameters.UserAssetName ?? string.Empty), 1 }}
                            )
                        }}
                    )
                );
            })
            .AddWithdrawal((options, parameters) =>
            {
                options.From = "withdrawal";
                options.Amount = 0;
                options.SetRedeemerFactory(withdrawRedeemer);
            })
            .Build();

        return multiSigLend;
    }

    public Func<CancelMintParams, Task<Transaction>> Cancel()
    {
        string mainValidatorAddress = "addr_test1wra8f56lfvx53trz3zk9e6n3728gqat945ycg53j7g5kvrc5qqfl0";
        string mainValidatorScriptRef = "5d84910a2e0ece53b64fe2bf0f0d3cdc8f32993d3a5b3fee7c15a8e237fc9e16";

        string cancelValidatorAddress = "addr_test1wzgvnetp806a7ff5v3cerwqk5pcwe7axax7uc424mpzm4ns08tkm5";
        string cancelValidatorScriptRef = "4af67781d38bbbc481538810889bd97c5888d9a19aa7122d27e435204003f4d7";
        string cancelValidatorRewardAddress = "stake_test17zgvnetp806a7ff5v3cerwqk5pcwe7axax7uc424mpzm4ns004wv7";

        string mintValidatorAddress = "addr_test1wrmdu7kq9kf3azpley0gx5cn6sjw7gdqs2az5zj9dnwu7dcde0xmg";
        string mintValidatorScriptRef = "491f084536901de3dcae4e066a9f3ccbbb9fa3fa21786db5978202c4736b9b98";
        string mintValidatorRewardAddress = "stake_test17rmdu7kq9kf3azpley0gx5cn6sjw7gdqs2az5zj9dnwu7dcd337vz";
        RedeemerDataBuilder<CancelMintParams, LevvyAction> cancelRedeemerBuilder1 = (mapping, parameters) =>
        {
            var (InputIndex, OutputIndexes) = mapping.GetInput("lockedUtxo1");
            InputIndices inputIndices = new((int)InputIndex, new Some<int>(0));
            LevvyOutputIndices outputIndices = new((int)OutputIndexes["cancelOutput1"], new None<int>(), new None<int>(), new None<int>());

            ActionParams actionParams = new(inputIndices, new None<int>(), new None<int>(), outputIndices, new Token());

            return new CancelAction(actionParams);
        };

        RedeemerDataBuilder<CancelMintParams, MintRedeemer> mintRedeemerBuilder = (mapping, parameters) =>
        {
            byte[] policyId = Convert.FromHexString(parameters.MintPolicy ?? string.Empty);

            return new MintRedeemer(policyId, new None<int>(), new None<MintOutputIndices>());
        };

        RedeemerDataBuilder<CancelMintParams, CborIndefList<Outref>> withdrawRedeemer = (mapping, parameters) =>
        {
            Outref outref1 = new(Convert.FromHexString("839857a9581d171210cf369eed0ed8d81fc7e2007658324ba76eb054fe219afc"), 0);
            Outref outref2 = new(Convert.FromHexString("839857a9581d171210cf369eed0ed8d81fc7e2007658324ba76eb054fe219afc"), 1);
            Outref outref3 = new(Convert.FromHexString("839857a9581d171210cf369eed0ed8d81fc7e2007658324ba76eb054fe219afc"), 2);

            CborIndefList<Outref> outrefs = new([outref1, outref2, outref3]);

            return outrefs;
        };

        RedeemerDataBuilder<CancelMintParams, PolicyId> mintWithdrawRedeemer = (mapping, parameters) => new PolicyId(Convert.FromHexString(parameters.MintPolicy));

        Console.WriteLine(ChangeAddress);
        var cancel = TransactionTemplateBuilder<CancelMintParams>.Create(provider)
            .AddStaticParty("change", ChangeAddress, true)
            .AddStaticParty("mainValidator", mainValidatorAddress)
            .AddStaticParty("cancelValidator", cancelValidatorAddress)
            .AddStaticParty("cancelValidatorRewardAddress", cancelValidatorRewardAddress)
            .AddStaticParty("mintValidator", mintValidatorAddress)
            .AddStaticParty("mintValidatorRewardAddress", mintValidatorRewardAddress)
            .AddRequiredSigner("change")
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
             .AddReferenceInput((options, parameters) =>
            {
                options.From = "mintValidator";
                options.UtxoRef = new TransactionInput(
                   Convert.FromHexString(mintValidatorScriptRef), 0
                );
                options.Id = "mintValidator";
            })
            .AddMint((options, parameters) =>
            {
                options.Policy = parameters.MintPolicy ?? string.Empty;
                options.Assets = new Dictionary<string, int>
                {
                    { parameters.UserAssetName ?? string.Empty, -1 },
                    { parameters.ReferenceAssetName ?? string.Empty, -1 }
                };
                options.SetRedeemerBuilder(mintRedeemerBuilder);
            })
            .AddInput((options, parameters) =>
            {
                options.From = "change";
            })
            .AddInput((options, parameters) =>
            {
                options.From = "mainValidator";
                options.UtxoRef = parameters.lockedUtxos[0];
                options.Id = "lockedUtxo1";
                options.SetRedeemerBuilder(cancelRedeemerBuilder1);
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "change";
                options.Amount = parameters.principalAmount;
                options.AssociatedInputId = "lockedUtxo1";
                options.Id = "cancelOutput1";
            })
            .AddWithdrawal((options, parameters) =>
            {
                options.From = "cancelValidatorRewardAddress";
                options.Amount = 0;
                options.SetRedeemerFactory(withdrawRedeemer);
            })
            .AddWithdrawal((options, parameters) =>
            {
                options.From = "mintValidatorRewardAddress";
                options.Amount = 0;
                options.SetRedeemerFactory(mintWithdrawRedeemer);
            })
            .Build();

        return cancel;
    }

    public Func<BorrowMintParams, Task<Transaction>> Borrow()
    {
        string mainValidatorAddress = "addr_test1wra8f56lfvx53trz3zk9e6n3728gqat945ycg53j7g5kvrc5qqfl0";
        string mainValidatorScriptRef = "5d84910a2e0ece53b64fe2bf0f0d3cdc8f32993d3a5b3fee7c15a8e237fc9e16";

        string mintValidatorAddress = "addr_test1wrmdu7kq9kf3azpley0gx5cn6sjw7gdqs2az5zj9dnwu7dcde0xmg";
        string mintValidatorScriptRef = "491f084536901de3dcae4e066a9f3ccbbb9fa3fa21786db5978202c4736b9b98";
        string mintValidatorRewardAddress = "stake_test17rmdu7kq9kf3azpley0gx5cn6sjw7gdqs2az5zj9dnwu7dcd337vz";

        string borrowValidatorAddress = "addr_test1wq2gmfnfs3u9mrz52munwsrdmrznk79ua40kpagmv9gas7gtzfurs";
        string borrowValidatorScriptRef = "58992377ab39360724fa3f992f15597cbdbc72756aa2b2026e668d528327a001";
        string borrowValidatorRewardAddress = "stake_test17q2gmfnfs3u9mrz52munwsrdmrznk79ua40kpagmv9gas7gt2hy56";

        string protocolParamsAddress = "addr_test1wr00dqehse7tfu0etd4cz8ldhlxaw7qdzz54esrjqacg36sp45dt3";

        RedeemerDataBuilder<BorrowMintParams, LevvyAction> borrowRedeemerBuilder = (mapping, parameters) =>
        {
            var (InputIndex, OutputIndexes) = mapping.GetInput("lockedUtxo1");
            InputIndices inputIndices = new((int)InputIndex, new None<int>());
            LevvyOutputIndices outputIndices = new((int)OutputIndexes["borrowOutput1"], new Some<int>((int)OutputIndexes["feeOutput"]), new None<int>(), new Some<int>((int)OutputIndexes["borrowOutput1"]));
            ActionParams actionParams = new(inputIndices, new Some<int>((int)mapping.GetReferenceInput("protocolParams")), new None<int>(), outputIndices, new Token());

            return new BorrowAction(actionParams);
        };

        RedeemerDataBuilder<BorrowMintParams, CborIndefList<Outref>> withdrawRedeemer = (mapping, parameters) =>
        {
            Outref outref1 = new(Convert.FromHexString("591b38da8316a9f2f331f7ef37c510f8baedf09b37a03b1ccad751897d1e6199"), 0);
            CborIndefList<Outref> outrefs = new([outref1]);

            return outrefs;
        };

        RedeemerDataBuilder<BorrowMintParams, MintRedeemer> mintRedeemerBuilder = (mapping, parameters) =>
        {
            byte[] policyId = Convert.FromHexString(parameters.MintPolicy ?? string.Empty);

            return new MintRedeemer(policyId, new Some<int>((int)mapping.GetReferenceInput("protocolParams")), new Some<MintOutputIndices>(new([1])));
        };

        RedeemerDataBuilder<BorrowMintParams, PolicyId> mintWithdrawRedeemer = (mapping, parameters) => new PolicyId(Convert.FromHexString(parameters.MintPolicy));

        var borrow = TransactionTemplateBuilder<BorrowMintParams>.Create(provider)
            .AddStaticParty("change", ChangeAddress, true)
            .AddStaticParty("mainValidator", mainValidatorAddress)
            .AddStaticParty("borrowValidator", borrowValidatorAddress)
            .AddStaticParty("borrowValidatorRewardAddress", borrowValidatorRewardAddress)
            .AddStaticParty("protocolParamsAddress", protocolParamsAddress)
            .AddStaticParty("mintValidator", mintValidatorAddress)
            .AddStaticParty("mintValidatorRewardAddress", mintValidatorRewardAddress)
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
                options.From = "mintValidator";
                options.UtxoRef = new TransactionInput(
                    Convert.FromHexString(mintValidatorScriptRef),
                    0
                );
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
            .AddMint((options, parameters) =>
            {
                options.Policy = parameters.MintPolicy ?? string.Empty;
                options.Assets = new Dictionary<string, int>
                {
                    { parameters.UserAssetName ?? string.Empty, 1 },
                    { parameters.ReferenceAssetName ?? string.Empty, 1 }
                };
                options.SetRedeemerBuilder(mintRedeemerBuilder);
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
            // Locked Assets
            .AddOutput((options, parameters) =>
            {
                options.To = "mainValidator";
                options.Amount = new LovelaceWithMultiAsset(
                    new Lovelace(2_000_000),
                    new MultiAssetOutput(new Dictionary<byte[], TokenBundleOutput>
                    {
                        {
                            Convert.FromHexString(parameters.MintPolicy),
                            new TokenBundleOutput(new Dictionary<byte[], ulong>
                            {
                                { Convert.FromHexString(parameters.LockedReferenceAssetName), 1 }
                            })
                        }
                    })
                );
                options.Datum = new InlineDatumOption(1, new CborEncodedValue(CborSerializer.Serialize(CborSerializer.Deserialize<PlutusData>(CborSerializer.Serialize(parameters.NftPositionDatum)))));
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "mainValidator";
                options.Amount = new LovelaceWithMultiAsset(
                    new Lovelace(parameters.CollateralDetails.Amount),
                    new MultiAssetOutput(new Dictionary<byte[], TokenBundleOutput>
                    {
                        {
                            Convert.FromHexString(parameters.MintPolicy),
                            new TokenBundleOutput(new Dictionary<byte[], ulong>
                            {
                                { Convert.FromHexString(parameters.ReferenceAssetName), 1 }
                            })
                        }
                    })
                );
                options.Datum = new InlineDatumOption(1, new CborEncodedValue(CborSerializer.Serialize(CborSerializer.Deserialize<PlutusData>(CborSerializer.Serialize(parameters.NftPositionDatum)))));
                options.AssociatedInputId = "lockedUtxo1";
                options.Id = "borrowOutput1";
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "change";
                options.Amount = new LovelaceWithMultiAsset(
                    new Lovelace(2_000_000),
                    new MultiAssetOutput(new Dictionary<byte[], TokenBundleOutput>
                    {
                        {
                            Convert.FromHexString(parameters.MintPolicy),
                            new TokenBundleOutput(new Dictionary<byte[], ulong>
                            {
                                { Convert.FromHexString(parameters.UserAssetName), 1 }
                            })
                        }
                    })
                );
            })
            .AddOutput((options, parameters) =>
            {
                options.To = "change";
                options.Amount = new Lovelace(2_000_000);
                options.AssociatedInputId = "lockedUtxo1";
                options.Id = "feeOutput";
                options.Datum = new InlineDatumOption(1, new CborEncodedValue(CborSerializer.Serialize(new CborDatumTag(Convert.FromHexString("2DA58A063A84F5D6CE1B9B5148EBBCCF54EFCC1B4FE157FBABFFB6F59A5ECFF6")))));
            })
            .AddWithdrawal((options, parameters) =>
            {
                options.From = "borrowValidatorRewardAddress";
                options.Amount = 0;
                options.SetRedeemerFactory(withdrawRedeemer);
            })
            .AddWithdrawal((options, parameters) =>
            {
                options.From = "mintValidatorRewardAddress";
                options.Amount = 0;
                options.SetRedeemerFactory(mintWithdrawRedeemer);
            })
            .SetValidFrom(1000)
            .Build();

        return borrow;
    }
}