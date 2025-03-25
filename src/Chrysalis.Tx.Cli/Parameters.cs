using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli;


public record LockParameters(Value Amount, DatumOption Datum);
public record UnlockParameters(TransactionInput LockedUtxoOutRef, TransactionInput ScriptRefUtxoOutref,  Redeemers Redeemer, Value Amount, ulong WithdrawalAmount, Redeemers WithdrawRedeemer);

public record TransferParameters(ulong Amount, Dictionary<string, string> Parties) : IParameters{
    public ulong Amount { get; init; } = Amount;

    public Dictionary<string, string> Parties { get; set; } = Parties;
}