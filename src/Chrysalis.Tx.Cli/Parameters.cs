using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli;

/// <summary>
/// Parameters for locking funds in a script.
/// </summary>
/// <param name="Amount">The value to lock.</param>
/// <param name="Datum">The datum to attach.</param>
internal sealed record LockParameters(Value Amount, DatumOption Datum);

/// <summary>
/// Parameters for unlocking funds from a script.
/// </summary>
/// <param name="LockedUtxoOutRef">The locked UTxO reference.</param>
/// <param name="ScriptRefUtxoOutref">The script reference UTxO.</param>
/// <param name="Redeemer">The redeemer for script validation.</param>
/// <param name="MainAmount">The main output value.</param>
/// <param name="FeeAmount">The fee output value.</param>
/// <param name="ChangeAmount">The change output value.</param>
/// <param name="WithdrawalAmount">The withdrawal amount.</param>
/// <param name="MainDatum">The main output datum.</param>
/// <param name="FeeDatum">The fee output datum.</param>
/// <param name="ChangeDatum">The change output datum.</param>
/// <param name="WithdrawRedeemer">The withdrawal redeemer.</param>
internal sealed record UnlockParameters(
    TransactionInput LockedUtxoOutRef,
    TransactionInput ScriptRefUtxoOutref,
    RedeemerMap? Redeemer, Value MainAmount,
    Value FeeAmount, Value ChangeAmount,
    ulong WithdrawalAmount,
    InlineDatumOption MainDatum,
    InlineDatumOption FeeDatum,
    InlineDatumOption ChangeDatum,
    RedeemerMap? WithdrawRedeemer
);

/// <summary>
/// Parameters for a simple transfer transaction.
/// </summary>
/// <param name="Amount">The amount to transfer.</param>
/// <param name="Parties">The parties involved with their addresses and change flags.</param>
internal sealed record TransferParameters(ulong Amount, Dictionary<string, (string address, bool isChange)> Parties) : ITransactionParameters
{
    /// <summary>
    /// Gets the transfer amount.
    /// </summary>
    public ulong Amount { get; init; } = Amount;

    /// <summary>
    /// Gets or sets the parties involved in the transfer.
    /// </summary>
    public Dictionary<string, (string address, bool isChange)> Parties { get; set; } = Parties;
}
