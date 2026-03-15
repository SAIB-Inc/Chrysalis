using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;

namespace Chrysalis.Tx.Builders;

/// <summary>
/// Tracks witness requirements accumulated during transaction building.
/// Each input, mint, withdrawal, or certificate declares what witnesses it needs.
/// </summary>
public sealed class WitnessRequirements
{
    /// <summary>Verification key hashes that must sign the transaction.</summary>
    public HashSet<string> VKeyHashes { get; } = [];

    /// <summary>Script hashes that must be provided in the witness set.</summary>
    public HashSet<string> ScriptHashes { get; } = [];

    /// <summary>Script hashes satisfied by reference inputs (excluded from witness set).</summary>
    public HashSet<string> ScriptRefHashes { get; } = [];

    /// <summary>Scripts to include in the witness set.</summary>
    public List<IScript> ScriptWitnesses { get; } = [];

    /// <summary>Plutus datums to include in the witness set.</summary>
    public List<IPlutusData> Datums { get; } = [];

    /// <summary>Required signer key hashes (for Plutus scripts that check signatories).</summary>
    public List<string> RequiredSigners { get; } = [];

    /// <summary>Redeemer data associated with this input/action.</summary>
    public IPlutusData? RedeemerData { get; set; }

    /// <summary>
    /// Merges another set of witness requirements into this one.
    /// </summary>
    public void Add(WitnessRequirements other)
    {
        ArgumentNullException.ThrowIfNull(other);

        foreach (string vk in other.VKeyHashes)
        {
            _ = VKeyHashes.Add(vk);
        }

        foreach (string sh in other.ScriptHashes)
        {
            _ = ScriptHashes.Add(sh);
        }

        foreach (string sr in other.ScriptRefHashes)
        {
            _ = ScriptRefHashes.Add(sr);
            _ = ScriptHashes.Remove(sr);
        }

        ScriptWitnesses.AddRange(other.ScriptWitnesses);
        Datums.AddRange(other.Datums);
        RequiredSigners.AddRange(other.RequiredSigners);
    }

    /// <summary>
    /// Estimates the witness set size in bytes for fee calculation.
    /// Each vkey witness is ~102 bytes (32 pubkey + 64 sig + 6 CBOR overhead).
    /// </summary>
    public int EstimatedWitnessSize()
    {
        const int vkeyWitnessSize = 102;
        return VKeyHashes.Count * vkeyWitnessSize;
    }

    /// <summary>
    /// Returns true if any Plutus scripts are involved (redeemers, script witnesses).
    /// </summary>
    public bool HasPlutusScripts => RedeemerData is not null || ScriptWitnesses.Count > 0;
}
