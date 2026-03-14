using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Extensions;

namespace Chrysalis.Tx.Builders;

/// <summary>
/// Result of building an input with its witness requirements.
/// </summary>
public sealed record InputBuilderResult(
    TransactionInput Input,
    ITransactionOutput Utxo,
    WitnessRequirements Requirements);

/// <summary>
/// Builds a transaction input with type-safe witness requirement tracking.
/// Follows CML's SingleInputBuilder pattern — each input declares upfront
/// what witnesses it needs (payment key, native script, or Plutus script).
/// </summary>
public sealed class InputBuilder(TransactionInput input, ITransactionOutput utxo)
{
    private readonly TransactionInput _input = input;
    private readonly ITransactionOutput _utxo = utxo;

    /// <summary>
    /// Standard payment key input — no scripts, just a signature.
    /// </summary>
    public InputBuilderResult PaymentKey() =>
        new(_input, _utxo, new WitnessRequirements());

    /// <summary>
    /// Native script input — requires the script in the witness set.
    /// </summary>
    public InputBuilderResult NativeScript(INativeScript script)
    {
        WitnessRequirements reqs = new();
        _ = reqs.ScriptHashes.Add(script.HashHex());
        return new(_input, _utxo, reqs);
    }

    /// <summary>
    /// Plutus script input with an explicit datum (for UTxOs locked with a datum hash).
    /// </summary>
    public InputBuilderResult PlutusScript(
        IScript script,
        IPlutusData redeemer,
        IPlutusData datum,
        params string[] requiredSigners)
    {
        WitnessRequirements reqs = new() { RedeemerData = redeemer };
        _ = reqs.ScriptHashes.Add(script.HashHex());
        reqs.ScriptWitnesses.Add(script);
        reqs.Datums.Add(datum);
        AddSigners(reqs, requiredSigners);
        return new(_input, _utxo, reqs);
    }

    /// <summary>
    /// Plutus script input with an inline datum (datum is in the UTxO, not in the witness set).
    /// </summary>
    public InputBuilderResult PlutusScriptInlineDatum(
        IScript script,
        IPlutusData redeemer,
        params string[] requiredSigners)
    {
        WitnessRequirements reqs = new() { RedeemerData = redeemer };
        _ = reqs.ScriptHashes.Add(script.HashHex());
        reqs.ScriptWitnesses.Add(script);
        AddSigners(reqs, requiredSigners);
        return new(_input, _utxo, reqs);
    }

    /// <summary>
    /// Plutus script input using a reference script (script is in a reference input, not the witness set).
    /// </summary>
    public InputBuilderResult PlutusScriptRef(
        string scriptHash,
        IPlutusData redeemer,
        IPlutusData? datum = null,
        params string[] requiredSigners)
    {
        ArgumentNullException.ThrowIfNull(requiredSigners);
        WitnessRequirements reqs = new() { RedeemerData = redeemer };
        _ = reqs.ScriptRefHashes.Add(scriptHash);
        if (datum is not null)
        {
            reqs.Datums.Add(datum);
        }

        AddSigners(reqs, requiredSigners);
        return new(_input, _utxo, reqs);
    }

    private static void AddSigners(WitnessRequirements reqs, string[] signers)
    {
        ArgumentNullException.ThrowIfNull(signers);
        foreach (string signer in signers)
        {
            reqs.RequiredSigners.Add(signer);
        }
    }
}
