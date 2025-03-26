using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
namespace Chrysalis.Tx.Models;

public record Outref(string TxId, ulong Index)
{
    public string TxId { get; init; } = TxId;
    public ulong Index { get; init; } = Index;
}

public record InputOptions(string From, Value? MinAmount, DatumOption? Datum, Redeemers? Redeemer, TransactionInput? UtxoRef, bool IsReference = false, string? Id = null)
{
    public string From { get; set; } = From;
    public Value? MinAmount { get; set; } = MinAmount;
    public TransactionInput? UtxoRef { get; set; } = UtxoRef;
    public DatumOption? Datum { get; set; } = Datum;
    public Redeemers? Redeemer { get; set; } = Redeemer;
    public bool IsReference { get; set; } = IsReference;
    public string? Id { get; set; } = Id;
}

public class OutputOptions(string To, Value? Amount, DatumOption? Datum, string? AssociatedInputId = null, string? Id = null)
{
    public string To { get; set; } = To;
    public Value? Amount { get; set; } = Amount;
    public DatumOption? Datum { get; set; } = Datum;
    public string? AssociatedInputId { get; set; } = AssociatedInputId;
    public string? Id { get; set; } = Id;

}

public record WithdrawalOptions<T>
{
    public string From { get; set; } 
    public ulong Amount { get; set; }
    public Action<Dictionary<int, Dictionary<string, int>>, T, Dictionary<RedeemerKey, RedeemerValue>>? RedeemerGenerator { get; set; }
    public RedeemerMap? Redeemers { get; set; }

     public WithdrawalOptions(string from, ulong amount)
        {
            From = from;
            Amount = amount;
        }

}