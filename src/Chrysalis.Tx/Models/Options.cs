using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Utils;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;


namespace Chrysalis.Tx.Models;

public record InputOptions<T>
{
    public string From { get; set; } = string.Empty;
    public Value? MinAmount { get; set; }
    public TransactionInput? UtxoRef { get; set; }
    public DatumOption? Datum { get; set; }
    public string? Id { get; set; }
    public RedeemerMap? Redeemer { get; set; }

    public Func<InputOutputMapping, T, TransactionBuilder, Redeemer<CborBase>>? RedeemerBuilder { get; set; }

    public InputOptions<T> SetRedeemerBuilder<TData>(RedeemerDataBuilder<T, TData> factory, RedeemerTag tag = RedeemerTag.Spend)
        where TData : CborBase
    {
        RedeemerBuilder = (mapping, context, transactionBuilder) =>
        {
            TData data = factory(mapping, context, transactionBuilder);
            return new Redeemer<CborBase>(tag, 0, data, new(157374, 49443675));
        };
        return this;
    }
}

public record ReferenceInputOptions
{
    public string From { get; set; } = string.Empty;
    public TransactionInput? UtxoRef { get; set; }
    public string? Id { get; set; }
}


public record OutputOptions
{
    public string To { get; set; } = string.Empty;
    public Value? Amount { get; set; }
    public DatumOption? Datum { get; set; }
    public string? AssociatedInputId { get; set; }
    public string? Id { get; set; }
    public Script? Script { get; set; }
    public void SetDatum<T>(T datum)
        where T : CborBase
    {
        Datum = new InlineDatumOption(1, new CborEncodedValue(CborSerializer.Serialize(CborSerializer.Deserialize<PlutusData>(CborSerializer.Serialize(datum)))));
    }
    public TransactionOutput BuildOutput(Dictionary<string, string> parties, ulong adaPerUtxoByte)
    {
        Address address = new(WalletAddress.FromBech32(parties[To]).ToBytes());
        CborEncodedValue? script = Script is not null ? new CborEncodedValue(CborSerializer.Serialize(Script)) : null;
        TransactionOutput output = new AlonzoTransactionOutput(address, Amount ?? new Lovelace(1000000), null);

        if (Datum is not null || Script is not null)
        {
            output = new PostAlonzoTransactionOutput(address, Amount ?? new Lovelace(1000000), Datum, script);
        }

        ulong minLovelace = FeeUtil.CalculateMinimumLovelace(adaPerUtxoByte, CborSerializer.Serialize(output));
        ulong currentLovelace = output.Amount().Lovelace();

        while (currentLovelace < minLovelace)
        {
            Value amount = output.Amount();
            amount = amount switch
            {
                LovelaceWithMultiAsset multiAsset => new LovelaceWithMultiAsset(new Lovelace(minLovelace), multiAsset.MultiAsset),
                _ => new Lovelace(minLovelace)
            };

            output = output switch
            {
                AlonzoTransactionOutput alonzoOutput => new AlonzoTransactionOutput(alonzoOutput.Address, amount, alonzoOutput.DatumHash),
                PostAlonzoTransactionOutput postAlonzoOutput => new PostAlonzoTransactionOutput(postAlonzoOutput.Address, amount, postAlonzoOutput.Datum, postAlonzoOutput.ScriptRef),
                _ => throw new InvalidOperationException("Unsupported transaction output type")
            };

            minLovelace = FeeUtil.CalculateMinimumLovelace(adaPerUtxoByte, CborSerializer.Serialize(output));
            currentLovelace = output.Amount().Lovelace();
        }

        return output;
    }
}

public record MintOptions<T>
{
    public string Policy { get; set; } = string.Empty;
    public Dictionary<string, int> Assets { get; set; } = [];
    public RedeemerMap? Redeemer { get; set; }
    public Func<InputOutputMapping, T, TransactionBuilder, Redeemer<CborBase>>? RedeemerBuilder { get; set; }
    public MintOptions<T> SetRedeemerBuilder<TData>(RedeemerDataBuilder<T, TData> factory)
        where TData : CborBase
    {
        RedeemerBuilder = (mapping, context, transactionBuilder) =>
        {
            TData data = factory(mapping, context, transactionBuilder);
            return new Redeemer<CborBase>(RedeemerTag.Mint, 0, data, new(98397, 25938682));
        };
        return this;
    }


    public string? Id { get; set; }
}

public record WithdrawalOptions<T>
{
    public string From { get; set; } = string.Empty;
    public ulong Amount { get; set; }
    public string? Id { get; set; }
    public RedeemerMap? Redeemer { get; set; }
    public Func<InputOutputMapping, T, TransactionBuilder, Redeemer<CborBase>>? RedeemerBuilder { get; set; }
    public WithdrawalOptions<T> SetRedeemerBuilder<TData>(RedeemerDataBuilder<T, TData> factory, ExUnits? exUnits = null)
        where TData : CborBase
    {
        RedeemerBuilder = (mapping, context, transactionBuilder) =>
        {
            TData data = factory(mapping, context, transactionBuilder);
            return new Redeemer<CborBase>(RedeemerTag.Reward, 0, data, new ExUnits(1648071, 497378507));
        };
        return this;
    }

}