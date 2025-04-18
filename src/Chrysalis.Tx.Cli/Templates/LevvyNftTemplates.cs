using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Cli.Templates.Parameters;
using Chrysalis.Tx.Models;

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
    
    public Func<LendParams, Task<Transaction>> Lend()
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
}