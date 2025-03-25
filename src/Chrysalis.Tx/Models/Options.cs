using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;
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

public class OutputOptions(string To, Value? Amount, DatumOption? Datum, string? AssociatedInputId = null, string? Role = null)
{
    public string To { get; set; } = To;
    public Value? Amount { get; set; } = Amount;
    public DatumOption? Datum { get; set; } = Datum;
    public string? AssociatedInputId { get; set; } = AssociatedInputId;
    public string? Role { get; set; } = Role;

}

public class WithdrawalOptions(string From, ulong Amount, Redeemers? Redeemer)
{
    public string From { get; set; } = From;
    public ulong Amount { get; set; } = Amount;
    public Redeemers? Redeemer { get; set; } = Redeemer;

}