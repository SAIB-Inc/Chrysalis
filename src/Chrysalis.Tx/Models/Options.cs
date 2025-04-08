using Chrysalis.Cbor.Extensions.Cardano.Core.Common;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
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

    public Func<InputOutputMapping, T, Redeemer<CborBase>>? RedeemerBuilder { get; set; }

    public InputOptions<T> SetRedeemerBuilder<TData>(RedeemerDataBuilder<T, TData> factory, RedeemerTag tag = RedeemerTag.Spend)
        where TData : CborBase
    {
        RedeemerBuilder = (mapping, context) =>
        {
            TData data = factory(mapping, context);
            return new Redeemer<CborBase>(tag, 0, data, new(1400000, 100000000));
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
    public TransactionOutput BuildOutput(Dictionary<string, string> parties, ulong adaPerUtxoByte)
    {
        Address address = new(WalletAddress.FromBech32(parties[To]).ToBytes());
        CborEncodedValue? script = Script is not null ? new CborEncodedValue(CborSerializer.Serialize(Script)) : null;
        PostAlonzoTransactionOutput output = new(address, Amount ?? new Lovelace(2000000), Datum, script);
        ulong minLovelace = FeeUtil.CalculateMinimumLovelace(adaPerUtxoByte, CborSerializer.Serialize(output));
        Value minLovelaceValue = new Lovelace(minLovelace);

        if (Amount is not null)
        {
            minLovelaceValue = Amount switch
            {
                LovelaceWithMultiAsset multiAsset => multiAsset.Lovelace() >= minLovelace
                    ? multiAsset
                    : new LovelaceWithMultiAsset(new Lovelace(minLovelace), multiAsset.MultiAsset),
                _ => Amount.Lovelace() >= minLovelace
                    ? Amount
                    : new Lovelace(minLovelace)
            };
        }

        Amount = minLovelaceValue;

        return Datum is null && Script is null
            ? new AlonzoTransactionOutput(address, Amount, null)
            : new PostAlonzoTransactionOutput(address, Amount, Datum, script);

    }
}

public record MintOptions<T>
{
    public string Policy { get; set; } = string.Empty;
    public Dictionary<string, ulong> Assets { get; set; } = [];
    public RedeemerMap? Redeemer { get; set; }
    public Func<InputOutputMapping, T, Redeemer<CborBase>>? RedeemerBuilder { get; set; }
    public MintOptions<T> SetRedeemerBuilder<TData>(RedeemerDataBuilder<T, TData> factory)
        where TData : CborBase
    {
        RedeemerBuilder = (mapping, context) =>
        {
            TData data = factory(mapping, context);
            return new Redeemer<CborBase>(RedeemerTag.Mint, 0, data, new(1400000, 100000000));
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
    public Func<InputOutputMapping, T, Redeemer<CborBase>>? RedeemerBuilder { get; set; }
    public WithdrawalOptions<T> SetRedeemerFactory<TData>(RedeemerDataBuilder<T, TData> factory, ExUnits? exUnits = null)
        where TData : CborBase
    {
        RedeemerBuilder = (mapping, context) =>
        {
            TData data = factory(mapping, context);
            return new Redeemer<CborBase>(RedeemerTag.Reward, 0, data, new ExUnits(1400000, 100000000));
        };
        return this;
    }

}