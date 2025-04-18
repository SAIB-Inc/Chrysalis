using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Cli.Templates.Parameters;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Cli.Templates.Models;
using Chrysalis.Tx.Cli.Templates.Parameters.NftPosition;

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

        string mintValidatorAddress = "addr_test1wrmdu7kq9kf3azpley0gx5cn6sjw7gdqs2az5zj9dnwu7dcde0xmg";
        string mintValidatorScriptRef = "491f084536901de3dcae4e066a9f3ccbbb9fa3fa21786db5978202c4736b9b98";

        string protocolParamsAddress = "addr_test1wr00dqehse7tfu0etd4cz8ldhlxaw7qdzz54esrjqacg36sp45dt3";

        RedeemerDataBuilder<LendMintParams, MintRedeemer> mintRedeemerBuilder = (mapping, parameters) =>
        {
            byte[] policyId = Convert.FromHexString(parameters.MintPolicy ?? string.Empty);
            var mappings = mapping.GetMappings();
            var output_indexes = mappings
                .SelectMany(e => e.Value.OutputIndexes)
                .ToDictionary();
            int mintOutputIndex1 = (int)output_indexes["mintOutput1"];
            int mintOutputIndex2 = (int)output_indexes["mintOutput2"];

            return new MintRedeemer(policyId, new Some<int>((int)mapping.GetReferenceInput("protocolParams")), new Some<IEnumerable<int>>([mintOutputIndex1, mintOutputIndex2]));
        };

        var multiSigLend = TransactionTemplateBuilder<LendMintParams>.Create(provider)
            .AddStaticParty("change", ChangeAddress, true)
            .AddStaticParty("mainValidator", mainValidatorAddress)
            .AddStaticParty("mintValidator", mintValidatorAddress)
            .AddStaticParty("protocolParams", protocolParamsAddress)
            .AddRequiredSigner("mintValidator")
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
            .AddInput((options, parameters) =>
            {
                options.From = "change";
                options.UtxoRef = parameters.MintOutRef;
                options.Id = "mint";
            })
            .AddMint((options, parameters) =>
            {
                options.Policy = parameters.MintPolicy ?? string.Empty;
                options.Assets = new Dictionary<string, ulong>
                {
                    { parameters.UserAssetName ?? string.Empty, 1 },
                    { parameters.ReferenceAssetName ?? string.Empty, 1 }
                };
                options.SetRedeemerBuilder(mintRedeemerBuilder);
                options.Id = "mint";
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
                options.Id = "mintOutput1";
                options.AssociatedInputId = "mint";
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
                options.Id = "mintOutput2";
                options.AssociatedInputId = "mint";
            })
            .Build();

        return multiSigLend;
    }
}