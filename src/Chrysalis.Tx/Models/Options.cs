using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Protocol;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;


namespace Chrysalis.Tx.Models;

public record InputOptions<T>
{
    public string From { get; set; } = string.Empty;
    public Value? MinAmount { get; set; }
    public TransactionInput? UtxoRef { get; set; }
    public DatumOption? Datum { get; set; }
    public string? Id { get; set; }
    public bool IsReference { get; set; }
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

public record OutputOptions
{
    public string To { get; set; } = string.Empty;
    public Value? Amount { get; set; }
    public DatumOption? Datum { get; set; }
    public string? AssociatedInputId { get; set; }
    public string? Id { get; set; }
    public string? Script { get; set; }
    public TransactionOutput BuildOutput(Dictionary<string, string> parties)
    {
        var address = Wallet.Models.Addresses.Address.FromBech32(parties[To]);
        return new PostAlonzoTransactionOutput(new Address(address.ToBytes()), Amount!, Datum, Script is not null ? new CborEncodedValue(Convert.FromHexString(Script)) : null);
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