using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Byron;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models.Cbor;
using SAIB.Cbor.Serialization;
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

    internal List<ReadOnlyMemory<byte>> BootstrapAttributes { get; } = [];

    internal static WitnessRequirements FromBuilder(TransactionBuilder builder, IReadOnlyCollection<ResolvedInput> utxos)
    {
        WitnessRequirements result = new();
        HashSet<string> byronRoots = [];
        List<INativeScript> nativeScripts = [.. builder.NativeScripts ?? []];
        foreach (TransactionInput input in builder.Inputs)
        {
            ResolvedInput resolved = TransactionBuilderExtensions.Resolve(input, utxos);
            AddPayment(resolved);
            AddScriptRef(resolved);
        }
        foreach (TransactionInput input in builder.CollateralInputs ?? [])
        {
            AddPayment(TransactionBuilderExtensions.Resolve(input, utxos));
        }
        foreach (TransactionInput input in builder.ReferenceInputs ?? [])
        {
            AddScriptRef(TransactionBuilderExtensions.Resolve(input, utxos));
        }
        if (builder.CurrentWithdrawals is { } withdrawals)
        {
            foreach ((RewardAccount account, _) in withdrawals.Value)
            {
                AddCredential((account.Value.Span[0] & 0x10) != 0, account.Value.Span.Slice(1, 28));
            }
        }
        foreach (ICertificate certificate in builder.CertificatesList ?? [])
        {
            if (certificate is PoolRegistration registration)
            {
                AddCredential(false, registration.Operator.Span);
                foreach (ReadOnlyMemory<byte> owner in registration.PoolOwners)
                {
                    AddCredential(false, owner.Span);
                }
            }
            else if (certificate is PoolRetirement retirement)
            {
                AddCredential(false, retirement.PoolKeyHash.Span);
            }
            else if (CertificateCredential.WitnessCredential(certificate) is { } credential)
            {
                AddCredential(credential.Type == 1, credential.Hash.Span);
            }
        }
        foreach (Voter voter in builder.Votes?.Value.Keys.AsEnumerable() ?? [])
        {
            AddCredential(voter.Tag is 1 or 3, voter.Hash.Span);
        }
        foreach (ReadOnlyMemory<byte> key in builder.RequiredSigners ?? [])
        {
            AddCredential(false, key.Span);
        }
        if (builder.Mint is { } mint)
        {
            foreach ((ReadOnlyMemory<byte> policy, _) in mint.Value)
            {
                AddCredential(true, policy.Span);
            }
        }
        foreach (ProposalProcedure proposal in builder.ProposalProcedures ?? CborList.Create<ProposalProcedure>([]))
        {
            ReadOnlyMemory<byte>? policy = proposal.GovAction switch
            {
                ParameterChangeAction change => change.PolicyHash,
                TreasuryWithdrawalsAction withdrawal => withdrawal.PolicyHash,
                _ => null
            };
            if (policy is { } hash)
            {
                AddCredential(true, hash.Span);
            }
        }
        foreach (INativeScript script in nativeScripts)
        {
            if (result.ScriptHashes.Contains(script.HashHex()))
            {
                AddNativeKeys(script);
            }
        }
        return result;

        void AddCredential(bool script, ReadOnlySpan<byte> hash) => _ = (script ? result.ScriptHashes : result.VKeyHashes).Add(Convert.ToHexStringLower(hash));
        void AddPayment(ResolvedInput resolved)
        {
            ReadOnlyMemory<byte> address = resolved.Output.Address();
            int type = address.Span[0] >> 4;
            if (type == 8)
            {
                CborReader reader = new(ByronAddress.Read(address).Payload.Value.Span);
                reader.ReadBeginArray();
                _ = reader.ReadSize();
                string root = Convert.ToHexStringLower(reader.ReadByteString());
                ReadOnlySpan<byte> attributes = reader.Buffer;
                reader.SkipDataItem();
                if (byronRoots.Add(root))
                {
                    result.BootstrapAttributes.Add(attributes[..(attributes.Length - reader.Buffer.Length)].ToArray());
                }
            }
            else
            {
                AddCredential((type & 1) != 0, address.Span.Slice(1, 28));
            }
        }
        void AddScriptRef(ResolvedInput resolved)
        {
            if (resolved.Output is not PostAlonzoTransactionOutput { ScriptRef: { } script })
            {
                return;
            }
            CborReader reader = new(script.Value.Span);
            reader.ReadBeginArray();
            _ = reader.ReadSize();
            if (reader.ReadUInt64() == 0 && script.Deserialize<IScript>() is MultiSigScript native)
            {
                nativeScripts.Add(native.NativeScript);
            }
        }
        void AddNativeKeys(INativeScript script)
        {
            if (script is ScriptPubKey key)
            {
                AddCredential(false, key.KeyHash.Span);
                return;
            }
            IEnumerable<INativeScript> scripts = script switch
            {
                ScriptAll all => all.Scripts,
                ScriptAny any => any.Scripts,
                ScriptNOfK nOfK => nOfK.Scripts,
                _ => []
            };
            foreach (INativeScript child in scripts)
            {
                AddNativeKeys(child);
            }
        }
    }

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
