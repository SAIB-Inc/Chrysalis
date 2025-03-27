using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli;


public record LockParameters(Value Amount, DatumOption Datum);
public record UnlockParameters(
    TransactionInput LockedUtxoOutRef,
    TransactionInput ScriptRefUtxoOutref,
    RedeemerMap Redeemer, Value MainAmount,
    Value FeeAmount, Value ChangeAmount,
    ulong WithdrawalAmount,
    InlineDatumOption MainDatum,
    InlineDatumOption FeeDatum,
    InlineDatumOption ChangeDatum,
    RedeemerMap WithdrawRedeemer
);

public record TransferParameters(ulong Amount, Dictionary<string, (string address, bool isChange)> Parties) : ITransactionParameters
{
    public ulong Amount { get; init; } = Amount;

    public Dictionary<string, (string address, bool isChange)> Parties { get; set; } = Parties;
}