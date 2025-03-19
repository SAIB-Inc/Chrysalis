using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;
namespace Chrysalis.Tx.Models;

public record Outref(string TxId, ulong Index)
{
    public string TxId { get; init; } = TxId;
    public ulong Index { get; init; } = Index;
}

public record InputOptions(string From, Value? MinAmount, ICbor? Datum, ICbor? Redeemer, Outref? UtxoRef)
{
    public string From { get; set; } = From;
    public Value? MinAmount { get; set; } = MinAmount;
    public Outref? UtxoRef { get; set; } = UtxoRef;
    public ICbor? Datum { get; set; } = Datum;
    public ICbor? Redeemer { get; set; } = Redeemer;
}

public class OutputOptions(string To, Value? Amount, ICbor? Datum)
{
    public string To { get; set; } = To;
    public Value? Amount { get; set; } = Amount;
    public ICbor? Datum { get; set; } = Datum;

}