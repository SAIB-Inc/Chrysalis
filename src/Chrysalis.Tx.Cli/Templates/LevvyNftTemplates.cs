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
                    new Lovelace(2_000_000),
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

        RedeemerDataBuilder<CancelMintParams, LevvyAction> cancelRedeemerBuilder1 = (mapping, parameters) =>
        {
            var (InputIndex, OutputIndexes) = mapping.GetInput("lockedUtxo1");
            InputIndices inputIndices = new((int)InputIndex, new None<int>());
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
            Outref outref1 = new(Convert.FromHexString("06de79b9e917b4f075eeb15a911620ee037c46fe297de1da4176d6eef551d928"), 0);
            CborIndefList<Outref> outrefs = new([outref1]);

            return outrefs;
        };

        Console.WriteLine(ChangeAddress);
        var cancel = TransactionTemplateBuilder<CancelMintParams>.Create(provider)
            .AddStaticParty("change", ChangeAddress, true)
            .AddStaticParty("mainValidator", mainValidatorAddress)
            .AddStaticParty("cancelValidator", cancelValidatorAddress)
            .AddStaticParty("cancelValidatorRewardAddress", cancelValidatorRewardAddress)
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
            .Build();

        return cancel;
    }
}