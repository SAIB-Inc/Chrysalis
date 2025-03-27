using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
namespace Chrysalis.Tx.Models;

public record InputOptions<T>(
    string From,
    Value? MinAmount,
    DatumOption? Datum,
    RedeemerMap? Redeemer,
    Action<Dictionary<int, Dictionary<string, int>>, T, Dictionary<RedeemerKey, RedeemerValue>>? RedeemerBuilder,
    TransactionInput? UtxoRef,
    string? Id,
    bool IsReference = false
)
{
    public string From { get; set; } = From;
    public Value? MinAmount { get; set; } = MinAmount;
    public TransactionInput? UtxoRef { get; set; } = UtxoRef;
    public DatumOption? Datum { get; set; } = Datum;
    public RedeemerMap? Redeemer { get; set; } = Redeemer;
    public Action<Dictionary<int, Dictionary<string, int>>, T, Dictionary<RedeemerKey, RedeemerValue>>? RedeemerBuilder { get; set; } = RedeemerBuilder;
    public bool IsReference { get; set; } = IsReference;
    public string? Id { get; set; } = Id;
}

public record OutputOptions(string To, Value? Amount, DatumOption? Datum, string? AssociatedInputId = null, string? Id = null)
{
    public string To { get; set; } = To;
    public Value? Amount { get; set; } = Amount;
    public DatumOption? Datum { get; set; } = Datum;
    public string? AssociatedInputId { get; set; } = AssociatedInputId;
    public string? Id { get; set; } = Id;

}

public record MintOptions(string Policy, Dictionary<string, ulong> Assets)
{
    public string Policy { get; set; } = Policy;
    public Dictionary<string, ulong> Assets { get; set; } = Assets;
}

public record WithdrawalOptions<T>(
    string From,
    ulong Amount,
    Action<Dictionary<int, Dictionary<string, int>>, T, Dictionary<RedeemerKey, RedeemerValue>>? RedeemerBuilder,
    RedeemerMap? Redeemers
)
{
    public string From { get; set; } = From;
    public ulong Amount { get; set; } = Amount;
    public Action<Dictionary<int, Dictionary<string, int>>, T, Dictionary<RedeemerKey, RedeemerValue>>? RedeemerBuilder { get; set; } = RedeemerBuilder;
    public RedeemerMap? Redeemers { get; set; } = Redeemers;

}