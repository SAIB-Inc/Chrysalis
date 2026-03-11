using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Utils;
using VmPlutusData = Chrysalis.Plutus.Types.PlutusData;
using VmConstr = Chrysalis.Plutus.Types.PlutusDataConstr;
using VmMap = Chrysalis.Plutus.Types.PlutusDataMap;
using VmList = Chrysalis.Plutus.Types.PlutusDataList;
using VmInt = Chrysalis.Plutus.Types.PlutusDataInteger;
using VmBytes = Chrysalis.Plutus.Types.PlutusDataByteString;
using CodecPlutusData = Chrysalis.Codec.Types.Cardano.Core.Common.IPlutusData;

namespace Chrysalis.Tx.Extensions;

/// <summary>
/// Builds Plutus V3 ScriptContext as VM PlutusData from Codec transaction types.
/// Port of Aiken/uplc script_context.rs + to_plutus_data.rs.
/// </summary>
public static class ScriptContextBuilder
{
    // ────────────────────── VM PlutusData Helpers ──────────────────────

    private static VmConstr Constr(long tag, params VmPlutusData[] fields) => new(new BigInteger(tag), [.. fields]);

    private static VmConstr Constr(long tag, ImmutableArray<VmPlutusData> fields) => new(new BigInteger(tag), fields);

    private static VmConstr EmptyConstr(long tag) => new(new BigInteger(tag), []);

    private static VmList List(IEnumerable<VmPlutusData> items) => new([.. items]);

    private static VmList EmptyList() => new([]);

    private static VmMap Map(IEnumerable<(VmPlutusData Key, VmPlutusData Value)> entries) => new([.. entries]);

    private static VmMap EmptyMap() => new([]);

    private static VmInt Int(long value) => new(new BigInteger(value));

    private static VmInt Int(ulong value) => new(new BigInteger(value));

    private static VmBytes Bytes(ReadOnlyMemory<byte> value) => new(value);

    private static VmBytes EmptyBytes() => new(ReadOnlyMemory<byte>.Empty);

    private static VmConstr OptionSome(VmPlutusData value) => Constr(0, value);

    private static VmConstr OptionNone() => EmptyConstr(1);

    private static VmConstr BoolData(bool value) => EmptyConstr(value ? 1 : 0);

    // ────────────────────── Address Parsing ──────────────────────

    /// <summary>
    /// Converts a raw Cardano address to VM PlutusData.
    /// Shelley addresses: Constr 0 [payment_credential, staking_credential]
    /// </summary>
    private static VmConstr AddressToPlutusData(ReadOnlyMemory<byte> addressBytes)
    {
        ReadOnlySpan<byte> addr = addressBytes.Span;
        if (addr.Length == 0)
        {
            return EmptyConstr(0);
        }

        byte header = addr[0];
        int addressType = (header >> 4) & 0x0F;

        // Shelley addresses (types 0-7)
        if (addressType <= 7)
        {
            // Payment credential: bytes 1..29
            bool isScriptPayment = (addressType & 0x01) != 0;
            byte[] paymentHash = addr.Slice(1, 28).ToArray();
            VmPlutusData paymentCredential = isScriptPayment
                ? Constr(1, Bytes(paymentHash))  // ScriptHash
                : Constr(0, Bytes(paymentHash)); // PubKeyHash

            // Staking credential
            VmPlutusData stakingCredential;
            if (addr.Length > 29)
            {
                bool isScriptStake = (addressType & 0x02) != 0;
                // Check for pointer addresses (types 4, 5)
                if (addressType is 4 or 5)
                {
                    // Pointer address — decode variable-length integers
                    int offset = 29;
                    ulong slot = DecodeVariableLength(addr, ref offset);
                    ulong txIdx = DecodeVariableLength(addr, ref offset);
                    ulong certIdx = DecodeVariableLength(addr, ref offset);
                    stakingCredential = OptionSome(
                        Constr(1, Constr(0, Int(slot), Int(txIdx), Int(certIdx)))
                    );
                }
                else
                {
                    byte[] stakeHash = addr.Slice(29, 28).ToArray();
                    VmPlutusData stakeCredential = isScriptStake
                        ? Constr(1, Bytes(stakeHash))
                        : Constr(0, Bytes(stakeHash));
                    stakingCredential = OptionSome(Constr(0, stakeCredential));
                }
            }
            else
            {
                // Enterprise address (types 6, 7) — no staking part
                stakingCredential = OptionNone();
            }

            return Constr(0, paymentCredential, stakingCredential);
        }

        // Reward/stake address (types 14, 15 → header bytes 0xE0/0xE1/0xF0/0xF1)
        if (addressType is 14 or 15)
        {
            bool isScript = (header & 0x10) != 0;
            byte[] hash = addr.Slice(1, 28).ToArray();
            VmPlutusData credential = isScript
                ? Constr(1, Bytes(hash))
                : Constr(0, Bytes(hash));
            return Constr(0, credential);
        }

        // Byron addresses — not supported in Plutus, return empty
        return EmptyConstr(0);
    }

    private static ulong DecodeVariableLength(ReadOnlySpan<byte> data, ref int offset)
    {
        ulong result = 0;
        byte b;
        do
        {
            b = data[offset++];
            result = (result << 7) | (uint)(b & 0x7F);
        } while ((b & 0x80) != 0);
        return result;
    }

    /// <summary>
    /// Extracts the payment credential hash from a raw address.
    /// Returns null for non-Shelley addresses.
    /// </summary>
    private static byte[]? GetPaymentScriptHash(ReadOnlyMemory<byte> addressBytes)
    {
        ReadOnlySpan<byte> addr = addressBytes.Span;
        if (addr.Length < 29)
        {
            return null;
        }

        byte header = addr[0];
        int addressType = (header >> 4) & 0x0F;

        // Only script payment credentials
        return addressType <= 7 && (addressType & 0x01) != 0 ? addr.Slice(1, 28).ToArray() : null;
    }

    // ────────────────────── Value Conversion ──────────────────────

    /// <summary>
    /// Converts Codec Value to VM PlutusData (Map format for Plutus).
    /// Value = Map&lt;PolicyId, Map&lt;AssetName, Quantity&gt;&gt;
    /// </summary>
    private static VmMap ValueToPlutusData(IValue value)
    {
        List<(VmPlutusData Key, VmPlutusData Value)> entries = [];

        ulong lovelace = value switch
        {
            Lovelace l => l.Amount,
            LovelaceWithMultiAsset lma => lma.Amount,
            _ => 0
        };

        // ADA entry: empty policy id => { empty asset name => lovelace }
        if (lovelace > 0)
        {
            entries.Add((
                EmptyBytes(),
                Map([(EmptyBytes(), Int(lovelace))])
            ));
        }

        // Multi-asset entries
        if (value is LovelaceWithMultiAsset multiAsset && multiAsset.MultiAsset.Value is not null)
        {
            foreach ((ReadOnlyMemory<byte> policyId, TokenBundleOutput bundle) in
                multiAsset.MultiAsset.Value.OrderBy(kv => kv.Key, ByteMemoryComparer.Instance))
            {
                List<(VmPlutusData Key, VmPlutusData Value)> assetEntries = [];
                foreach ((ReadOnlyMemory<byte> assetName, ulong amount) in
                    bundle.Value.OrderBy(kv => kv.Key, ByteMemoryComparer.Instance))
                {
                    assetEntries.Add((Bytes(assetName), Int(amount)));
                }
                entries.Add((Bytes(policyId), Map(assetEntries)));
            }
        }

        return entries.Count == 0 ? EmptyMap() : Map(entries);
    }

    /// <summary>
    /// Converts Codec mint Value to VM PlutusData (signed quantities).
    /// </summary>
    private static VmMap MintToPlutusData(MultiAssetMint? mint)
    {
        if (mint?.Value is null || mint.Value.Value.Count == 0)
        {
            return EmptyMap();
        }

        List<(VmPlutusData Key, VmPlutusData Value)> entries = [];

        foreach ((ReadOnlyMemory<byte> policyId, TokenBundleMint bundle) in
            mint.Value.Value.OrderBy(kv => kv.Key, ByteMemoryComparer.Instance))
        {
            List<(VmPlutusData Key, VmPlutusData Value)> assetEntries = [];
            foreach ((ReadOnlyMemory<byte> assetName, long amount) in
                bundle.Value.OrderBy(kv => kv.Key, ByteMemoryComparer.Instance))
            {
                assetEntries.Add((Bytes(assetName), Int(amount)));
            }
            entries.Add((Bytes(policyId), Map(assetEntries)));
        }

        return Map(entries);
    }

    // ────────────────────── Output Conversion ──────────────────────

    /// <summary>
    /// Converts a Codec ITransactionOutput to VM PlutusData (V3 format).
    /// TxOut = Constr 0 [address, value, datum_option, script_ref_option]
    /// </summary>
    private static VmConstr OutputToPlutusData(ITransactionOutput output) => output switch
    {
        PostAlonzoTransactionOutput postAlonzo => Constr(0,
            AddressToPlutusData(postAlonzo.Address.Value),
            ValueToPlutusData(postAlonzo.Amount),
            DatumOptionToPlutusData(postAlonzo.Datum),
            ScriptRefToPlutusData(postAlonzo.ScriptRef)
        ),
        AlonzoTransactionOutput alonzo => Constr(0,
            AddressToPlutusData(alonzo.Address.Value),
            ValueToPlutusData(alonzo.Amount),
            alonzo.DatumHash is not null
                ? Constr(1, Bytes(alonzo.DatumHash.Value))  // OutputDatumHash
                : EmptyConstr(0),  // NoOutputDatum
            OptionNone()  // No script ref in Alonzo
        ),
        _ => throw new InvalidOperationException($"Unsupported output type: {output.GetType().Name}")
    };

    /// <summary>
    /// DatumOption: NoOutputDatum = Constr 0 [] | OutputDatumHash = Constr 1 [hash] | OutputDatum = Constr 2 [data]
    /// </summary>
    private static VmConstr DatumOptionToPlutusData(IDatumOption? datum) => datum switch
    {
        null => EmptyConstr(0),
        DatumHashOption dh => Constr(1, Bytes(dh.DatumHash)),
        InlineDatumOption inline => Constr(2, CodecPlutusDataToVm(
            inline.Data.Deserialize<CodecPlutusData>()
        )),
        _ => EmptyConstr(0)
    };

    /// <summary>
    /// ScriptRef: Option&lt;ScriptHash&gt; — parse [language, script_bytes], compute proper script hash.
    /// Port of Aiken ScriptRef::to_plutus_data (compute_hash for each script variant).
    /// </summary>
    private static VmConstr ScriptRefToPlutusData(CborEncodedValue? scriptRef)
    {
        if (scriptRef is null)
        {
            return OptionNone();
        }

        byte[] rawInner = scriptRef.GetValue();
        if (rawInner.Length < 2 || rawInner[0] != 0x82) // expect definite array of 2
        {
            return OptionNone();
        }

        int language = rawInner[1]; // 0=native, 1=V1, 2=V2, 3=V3

        if (language is >= 1 and <= 3)
        {
            // Plutus script: [language, script_bytestring]
            SAIB.Cbor.Serialization.CborReader reader = new(rawInner.AsSpan(2));
            byte[] scriptBytes = reader.ReadByteStringToArray();
            return OptionSome(Bytes(ComputeScriptHash(language, scriptBytes)));
        }

        if (language == 0)
        {
            // Native script: [0, native_script_cbor]
            ReadOnlySpan<byte> nativeScriptCbor = rawInner.AsSpan(2);
            return OptionSome(Bytes(ComputeScriptHash(0, nativeScriptCbor)));
        }

        return OptionNone();
    }

    /// <summary>
    /// Computes a Cardano script hash: blake2b-224(language_byte || script_bytes).
    /// </summary>
    private static byte[] ComputeScriptHash(int language, ReadOnlySpan<byte> scriptBytes)
    {
        byte[] prefixed = new byte[1 + scriptBytes.Length];
        prefixed[0] = (byte)language;
        scriptBytes.CopyTo(prefixed.AsSpan(1));
        return HashUtil.Blake2b224(prefixed);
    }

    // ────────────────────── Input Conversion ──────────────────────

    /// <summary>
    /// TransactionInput to VM PlutusData.
    /// TxOutRef = Constr 0 [tx_id, index]
    /// </summary>
    private static VmConstr TxOutRefToPlutusData(TransactionInput input) => Constr(0, Bytes(input.TransactionId), Int(input.Index));

    /// <summary>
    /// TxInInfo = Constr 0 [out_ref, resolved_output]
    /// </summary>
    private static VmConstr TxInInfoToPlutusData(TransactionInput input, ITransactionOutput output) => Constr(0, TxOutRefToPlutusData(input), OutputToPlutusData(output));

    // ────────────────────── Sorted Inputs ──────────────────────

    private static List<(TransactionInput Input, ITransactionOutput Output)> SortAndResolveInputs(
        IEnumerable<TransactionInput> inputs,
        IReadOnlyList<ResolvedInput> utxos) => [.. inputs
            .OrderBy(i => i.TransactionId, ByteMemoryComparer.Instance)
            .ThenBy(i => i.Index)
            .Select(input =>
            {
                ResolvedInput? resolved = utxos.FirstOrDefault(u =>
                    u.Outref.TransactionId.Span.SequenceEqual(input.TransactionId.Span)
                    && u.Outref.Index == input.Index);

                return resolved is not null
                    ? (input, resolved.Output)
                    : throw new InvalidOperationException(
                        $"Input not found in UTxO set: {Convert.ToHexString(input.TransactionId.Span)}#{input.Index}");
            })];

    // ────────────────────── Credential Conversion ──────────────────────

    private static VmConstr CredentialToPlutusData(Credential credential) =>
        // 0 = AddrKeyhash, 1 = ScriptHash
        credential.Type == 0
            ? Constr(0, Bytes(credential.Hash))
            : Constr(1, Bytes(credential.Hash));

    // ────────────────────── Codec PlutusData → VM PlutusData ──────────────────────

    /// <summary>
    /// Converts Codec PlutusData to VM PlutusData.
    /// </summary>
    public static VmPlutusData CodecPlutusDataToVm(CodecPlutusData codecData) => codecData switch
    {
        PlutusConstr constr => Constr(
            CborTagToConstrIndex(constr.ConstrIndex),
            constr.Fields.GetValue().Select(CodecPlutusDataToVm).ToImmutableArray()),

        PlutusMap map => Map(
            map.Value.Select(kv => (CodecPlutusDataToVm(kv.Key), CodecPlutusDataToVm(kv.Value)))),

        PlutusList list => List(
            list.Value.GetValue().Select(CodecPlutusDataToVm)),

        PlutusInt i32 => Int(i32.Value),
        PlutusInt64 i64 => Int(i64.Value),
        PlutusUint64 u64 => Int(u64.Value),
        PlutusBigUint bigU => new VmInt(new BigInteger(bigU.Value.Span, isUnsigned: true, isBigEndian: true)),
        PlutusBigNint bigN => new VmInt(-1 - new BigInteger(bigN.Value.Span, isUnsigned: true, isBigEndian: true)),

        PlutusBoundedBytes bb => Bytes(bb.Value),

        _ => throw new InvalidOperationException($"Unsupported Codec PlutusData type: {codecData.GetType().Name}")
    };

    /// <summary>
    /// Converts a raw CBOR alternative tag to a logical Plutus constructor index.
    /// CBOR tags 121-127 → indices 0-6, tags 1280-1400 → indices 7-127.
    /// </summary>
    private static long CborTagToConstrIndex(int cborTag) => cborTag switch
    {
        >= 121 and <= 127 => cborTag - 121,
        >= 1280 and <= 1400 => cborTag - 1280 + 7,
        _ => cborTag // Already a logical index or unknown
    };

    // ────────────────────── Time Range ──────────────────────

    private static VmConstr TimeRangeToPlutusData(ulong? validityStart, ulong? ttl, SlotNetworkConfig slotConfig)
    {
        VmPlutusData lowerBound = validityStart.HasValue
            ? Constr(0,
                Constr(1, Int(SlotToPosixTime(validityStart.Value, slotConfig))),
                BoolData(true))  // Finite lower bound is inclusive
            : Constr(0,
                EmptyConstr(0),  // NegInf
                BoolData(true)); // Infinite bounds are exclusive by convention

        VmPlutusData upperBound = ttl.HasValue
            ? Constr(0,
                Constr(1, Int(SlotToPosixTime(ttl.Value, slotConfig))),
                BoolData(false)) // Finite upper bound is exclusive
            : Constr(0,
                EmptyConstr(2),  // PosInf
                BoolData(true)); // Infinite bounds are exclusive by convention

        return Constr(0, lowerBound, upperBound);
    }

    private static ulong SlotToPosixTime(ulong slot, SlotNetworkConfig config)
    {
        ulong msAfterBegin = (slot - (ulong)config.ZeroSlot) * (ulong)config.SlotLength;
        return (ulong)config.ZeroTime + msAfterBegin;
    }

    // ────────────────────── ICertificate Conversion (V3) ──────────────────────

    private static VmConstr CertificateToPlutusData(ICertificate cert) => cert switch
    {
        // Reg = Constr 0 [credential, deposit_option]
        StakeRegistration reg => Constr(0,
            CredentialToPlutusData(reg.StakeCredential),
            OptionNone()),
        RegCert reg => Constr(0,
            CredentialToPlutusData(reg.StakeCredential),
            OptionNone()),

        // UnReg = Constr 1 [credential, deposit_option]
        StakeDeregistration dereg => Constr(1,
            CredentialToPlutusData(dereg.StakeCredential),
            OptionNone()),
        UnRegCert unreg => Constr(1,
            CredentialToPlutusData(unreg.StakeCredential),
            OptionNone()),

        // Deleg = Constr 2 [credential, delegatee]
        StakeDelegation deleg => Constr(2,
            CredentialToPlutusData(deleg.StakeCredential),
            Constr(0, Bytes(deleg.PoolKeyHash))),
        VoteDelegCert voteDeleg => Constr(2,
            CredentialToPlutusData(voteDeleg.StakeCredential),
            Constr(1, DRepToPlutusData(voteDeleg.DRep))),
        StakeVoteDelegCert svDeleg => Constr(2,
            CredentialToPlutusData(svDeleg.StakeCredential),
            Constr(2, Bytes(svDeleg.PoolKeyHash), DRepToPlutusData(svDeleg.DRep))),

        // RegDeleg = Constr 3 [credential, delegatee, deposit]
        StakeRegDelegCert srd => Constr(3,
            CredentialToPlutusData(srd.StakeCredential),
            Constr(0, Bytes(srd.PoolKeyHash)),
            Int(srd.Coin)),
        VoteRegDelegCert vrd => Constr(3,
            CredentialToPlutusData(vrd.StakeCredential),
            Constr(1, DRepToPlutusData(vrd.DRep)),
            Int(vrd.Coin)),
        StakeVoteRegDelegCert svrd => Constr(3,
            CredentialToPlutusData(svrd.StakeCredential),
            Constr(2, Bytes(svrd.PoolKeyHash), DRepToPlutusData(svrd.DRep)),
            Int(svrd.Coin)),

        // RegDRep = Constr 4 [credential, deposit]
        RegDrepCert regDrep => Constr(4,
            CredentialToPlutusData(regDrep.DRepCredential),
            Int(regDrep.Coin)),

        // UpdateDRep = Constr 5 [credential]
        UpdateDrepCert updateDrep => Constr(5,
            CredentialToPlutusData(updateDrep.DRepCredential)),

        // UnRegDRep = Constr 6 [credential, deposit]
        UnRegDrepCert unregDrep => Constr(6,
            CredentialToPlutusData(unregDrep.DRepCredential),
            Int(unregDrep.Coin)),

        // PoolRegistration = Constr 7 [operator, vrf_keyhash]
        PoolRegistration poolReg => Constr(7,
            Bytes(poolReg.Operator),
            Bytes(poolReg.VrfKeyHash)),

        // PoolRetirement = Constr 8 [pool_keyhash, epoch]
        PoolRetirement poolRet => Constr(8,
            Bytes(poolRet.PoolKeyHash),
            Int(poolRet.Epoch)),

        // AuthCommitteeHot = Constr 9 [cold_credential, hot_credential]
        AuthCommitteeHotCert authHot => Constr(9,
            CredentialToPlutusData(authHot.ColdCredential),
            CredentialToPlutusData(authHot.HotCredential)),

        // ResignCommitteeCold = Constr 10 [cold_credential]
        ResignCommitteeColdCert resign => Constr(10,
            CredentialToPlutusData(resign.ColdCredential)),

        _ => throw new InvalidOperationException($"Unsupported certificate type: {cert.GetType().Name}")
    };

    private static VmConstr DRepToPlutusData(IDRep drep) => drep switch
    {
        DRepAddrKeyHash key => Constr(0, Constr(0, Bytes(key.KeyHash))),
        DRepScriptHash script => Constr(0, Constr(1, Bytes(script.ScriptHash))),
        Abstain => EmptyConstr(1),
        DRepNoConfidence => EmptyConstr(2),
        _ => throw new InvalidOperationException($"Unknown IDRep type: {drep.GetType().Name}")
    };

    // ────────────────────── Voter/Vote Conversion ──────────────────────

    private static VmConstr VoterToPlutusData(Voter voter) =>
        // Voter tag: 0=ConstitutionalCommittee, 1=IDRep, 2=StakePool
        // But credential type is encoded in the hash lookup:
        // CC key → Constr 0 [Constr 0 [hash]]  (key credential)
        // CC script → Constr 0 [Constr 1 [hash]]  (script credential)
        // IDRep key → Constr 1 [Constr 0 [hash]]
        // IDRep script → Constr 1 [Constr 1 [hash]]
        // StakePool → Constr 2 [hash]

        // The Codec Voter stores Tag (0-4 in Aiken) but Chrysalis uses (0=CC, 1=IDRep, 2=StakePool)
        // with a separate credential type. Need to check the actual encoding.
        // Looking at the Voter record: Tag and Hash. The full Aiken voters are:
        // ConstitutionalCommitteeScript(hash), ConstitutionalCommitteeKey(hash),
        // DRepScript(hash), DRepKey(hash), StakePoolKey(hash)
        // Chrysalis Codec Voter(Tag, Hash) where Tag encodes the full variant.

        // Tag 0 = ConstitutionalCommitteeKey, 1 = ConstitutionalCommitteeScript (reversed from Aiken?)
        // Actually looking at the CDDL: voter = [0, addr_keyhash | 1, script_hash | 2, addr_keyhash | 3, script_hash | 4, addr_keyhash]
        // So: 0=CC hot key, 1=CC hot script, 2=IDRep key, 3=IDRep script, 4=SPO key
        voter.Tag switch
        {
            0 => Constr(0, Constr(0, Bytes(voter.Hash))), // CC key
            1 => Constr(0, Constr(1, Bytes(voter.Hash))), // CC script
            2 => Constr(1, Constr(0, Bytes(voter.Hash))), // IDRep key
            3 => Constr(1, Constr(1, Bytes(voter.Hash))), // IDRep script
            4 => Constr(2, Bytes(voter.Hash)),             // SPO key
            _ => throw new InvalidOperationException($"Unknown voter tag: {voter.Tag}")
        };

    private static VmConstr VoteToPlutusData(VotingProcedure procedure) =>
        // Vote: 0=No, 1=Yes, 2=Abstain
        EmptyConstr(procedure.Vote);

    private static VmConstr GovActionIdToPlutusData(GovActionId actionId) => Constr(0, Bytes(actionId.TransactionId), Int(actionId.GovActionIndex));

    // ────────────────────── Withdrawals ──────────────────────

    private static VmMap WithdrawalsToPlutusData(Withdrawals? withdrawals)
    {
        if (withdrawals?.Value is null || withdrawals.Value.Count == 0)
        {
            return EmptyMap();
        }

        List<(VmPlutusData Key, VmPlutusData Value)> entries = [];

        // Sort by parsed address components — port of Aiken sort_reward_accounts:
        // network first, then credential type (script < key), then hash
        foreach ((RewardAccount account, ulong coin) in
            withdrawals.Value.OrderBy(kv => kv.Key.Value, RewardAccountComparer.Instance))
        {
            entries.Add((CredentialFromRewardAccount(account), Int(coin)));
        }

        return Map(entries);
    }

    private static VmConstr CredentialFromRewardAccount(RewardAccount account)
    {
        ReadOnlySpan<byte> bytes = account.Value.Span;
        if (bytes.Length < 29)
        {
            return EmptyConstr(0);
        }

        byte header = bytes[0];
        bool isScript = (header & 0x10) != 0;
        byte[] hash = bytes.Slice(1, 28).ToArray();

        return isScript
            ? Constr(1, Bytes(hash))
            : Constr(0, Bytes(hash));
    }

    // ────────────────────── IScript Purpose / IScript Info ──────────────────────

    /// <summary>
    /// Builds ScriptPurpose from a redeemer key, matching against sorted inputs/mints/etc.
    /// Port of Aiken script_purpose_builder.
    /// </summary>
    private static VmConstr? BuildScriptPurpose(
        int tag, ulong index,
        List<(TransactionInput Input, ITransactionOutput Output)> sortedInputs,
        List<(ReadOnlyMemory<byte> PolicyId, TokenBundleMint Bundle)>? sortedMint,
        List<ICertificate> certificates,
        List<(RewardAccount Account, ulong Coin)> sortedWithdrawals,
        List<Voter> sortedVoters,
        List<ProposalProcedure> proposals)
    {
        int idx = (int)index;
        return tag switch
        {
            // Mint → Constr 0 [policy_id]
            1 => sortedMint is not null && idx < sortedMint.Count
                ? Constr(0, Bytes(sortedMint[idx].PolicyId))
                : null,

            // Spend → Constr 1 [out_ref, datum_option]
            0 => idx < sortedInputs.Count
                ? Constr(1, TxOutRefToPlutusData(sortedInputs[idx].Input))
                : null,

            // Reward → Constr 2 [credential]
            3 => idx < sortedWithdrawals.Count
                ? Constr(2, CredentialFromRewardAccount(sortedWithdrawals[idx].Account))
                : null,

            // Cert → Constr 3 [index, cert]
            2 => idx < certificates.Count
                ? Constr(3, Int(idx), CertificateToPlutusData(certificates[idx]))
                : null,

            // Vote → Constr 4 [voter]
            4 => idx < sortedVoters.Count
                ? Constr(4, VoterToPlutusData(sortedVoters[idx]))
                : null,

            // Propose → Constr 5 [index, procedure]
            5 => idx < proposals.Count
                ? Constr(5, Int(idx), ProposalProcedureToPlutusData(proposals[idx]))
                : null,

            _ => null
        };
    }

    /// <summary>
    /// Builds V3 ScriptInfo from a redeemer, with datum for Spend.
    /// ScriptInfo differs from ScriptPurpose in that Spend includes the datum.
    /// </summary>
    private static VmConstr? BuildScriptInfo(
        int tag, ulong index,
        List<(TransactionInput Input, ITransactionOutput Output)> sortedInputs,
        List<(ReadOnlyMemory<byte> PolicyId, TokenBundleMint Bundle)>? sortedMint,
        List<ICertificate> certificates,
        List<(RewardAccount Account, ulong Coin)> sortedWithdrawals,
        List<Voter> sortedVoters,
        List<ProposalProcedure> proposals,
        DataLookupTable lookupTable)
    {
        int idx = (int)index;
        return tag switch
        {
            // Mint → Constr 0 [policy_id]
            1 => sortedMint is not null && idx < sortedMint.Count
                ? Constr(0, Bytes(sortedMint[idx].PolicyId))
                : null,

            // Spend → Constr 1 [out_ref, datum_option]
            0 when idx < sortedInputs.Count => BuildSpendScriptInfo(
                sortedInputs[idx].Input,
                sortedInputs[idx].Output,
                lookupTable),

            // Reward → Constr 2 [credential]
            3 => idx < sortedWithdrawals.Count
                ? Constr(2, CredentialFromRewardAccount(sortedWithdrawals[idx].Account))
                : null,

            // Cert → Constr 3 [index, cert]
            2 => idx < certificates.Count
                ? Constr(3, Int(idx), CertificateToPlutusData(certificates[idx]))
                : null,

            // Vote → Constr 4 [voter]
            4 => idx < sortedVoters.Count
                ? Constr(4, VoterToPlutusData(sortedVoters[idx]))
                : null,

            // Propose → Constr 5 [index, procedure]
            5 => idx < proposals.Count
                ? Constr(5, Int(idx), ProposalProcedureToPlutusData(proposals[idx]))
                : null,

            _ => null
        };
    }

    private static VmConstr BuildSpendScriptInfo(
        TransactionInput input,
        ITransactionOutput output,
        DataLookupTable lookupTable)
    {
        VmPlutusData? datum = null;

        // Resolve datum from output
        if (output is PostAlonzoTransactionOutput postAlonzo)
        {
            datum = postAlonzo.Datum switch
            {
                DatumHashOption dh => ResolveDatum(dh.DatumHash, lookupTable),
                InlineDatumOption inline => CodecPlutusDataToVm(
                    inline.Data.Deserialize<CodecPlutusData>()),
                _ => null
            };
        }
        else if (output is AlonzoTransactionOutput alonzo && alonzo.DatumHash is not null)
        {
            datum = ResolveDatum(alonzo.DatumHash.Value, lookupTable);
        }

        // ScriptInfo::Spending = Constr 1 [out_ref, datum_option]
        VmPlutusData datumOption = datum is not null ? OptionSome(datum) : OptionNone();
        return Constr(1, TxOutRefToPlutusData(input), datumOption);
    }

    private static VmPlutusData? ResolveDatum(ReadOnlyMemory<byte> datumHash, DataLookupTable lookupTable)
    {
        byte[]? datumCbor = lookupTable.GetDatum(datumHash.ToArray());
        if (datumCbor is null)
        {
            return null;
        }

        CodecPlutusData codecDatum = CborSerializer.Deserialize<CodecPlutusData>(datumCbor);
        return CodecPlutusDataToVm(codecDatum);
    }

    private static VmConstr ProposalProcedureToPlutusData(ProposalProcedure procedure) => Constr(0,
            Int(procedure.Deposit),
            AddressToPlutusData(procedure.RewardAccount.Value),
            GovActionToPlutusData(procedure.GovAction));

    private static VmConstr GovActionToPlutusData(IGovAction action) => action switch
    {
        ParameterChangeAction pca => Constr(0,
            pca.ActionId is not null ? OptionSome(GovActionIdToPlutusData(pca.ActionId.Value)) : OptionNone(),
            EmptyMap(), // TODO: ProtocolParamUpdate to PlutusData
            pca.PolicyHash is not null ? OptionSome(Bytes(pca.PolicyHash.Value)) : OptionNone()),

        HardForkInitiationAction hf => Constr(1,
            hf.ActionId is not null ? OptionSome(GovActionIdToPlutusData(hf.ActionId.Value)) : OptionNone(),
            List([Int(hf.ProtocolVersion.Major), Int(hf.ProtocolVersion.SequenceNumber)])),

        TreasuryWithdrawalsAction tw => Constr(2,
            WithdrawalsMapToPlutusData(tw.Withdrawals),
            tw.PolicyHash is not null ? OptionSome(Bytes(tw.PolicyHash.Value)) : OptionNone()),

        NoConfidence nc => Constr(3,
            nc.ActionId is not null ? OptionSome(GovActionIdToPlutusData(nc.ActionId.Value)) : OptionNone()),

        UpdateCommittee uc => Constr(4,
            uc.ActionId is not null ? OptionSome(GovActionIdToPlutusData(uc.ActionId.Value)) : OptionNone(),
            EmptyList(), // removed members
            EmptyMap(),  // added members
            EmptyConstr(0)), // quorum

        NewConstitution nca => Constr(5,
            nca.ActionId is not null ? OptionSome(GovActionIdToPlutusData(nca.ActionId.Value)) : OptionNone(),
            Constr(0, nca.Constitution.GuardrailsScriptHash is not null
                ? OptionSome(Bytes(nca.Constitution.GuardrailsScriptHash.Value))
                : OptionNone())),

        InfoAction => EmptyConstr(6),

        _ => throw new InvalidOperationException($"Unsupported IGovAction type: {action.GetType().Name}")
    };

    private static VmMap WithdrawalsMapToPlutusData(Dictionary<RewardAccount, ulong> withdrawals)
    {
        if (withdrawals is null || withdrawals.Count == 0)
        {
            return EmptyMap();
        }

        List<(VmPlutusData Key, VmPlutusData Value)> entries = [];
        foreach ((RewardAccount account, ulong coin) in withdrawals)
        {
            entries.Add((AddressToPlutusData(account.Value), Int(coin)));
        }
        return Map(entries);
    }

    // ────────────────────── Redeemer/Data Info ──────────────────────

    private static VmMap RedeemersToPlutusData(
        List<RedeemerInfo> redeemers,
        List<(TransactionInput Input, ITransactionOutput Output)> sortedInputs,
        List<(ReadOnlyMemory<byte> PolicyId, TokenBundleMint Bundle)>? sortedMint,
        List<ICertificate> certificates,
        List<(RewardAccount Account, ulong Coin)> sortedWithdrawals,
        List<Voter> sortedVoters,
        List<ProposalProcedure> proposals)
    {
        List<(VmPlutusData Key, VmPlutusData Value)> entries = [];

        // Sort redeemers by (tag, index)
        IOrderedEnumerable<RedeemerInfo> sorted = redeemers.OrderBy(r => r.Tag).ThenBy(r => r.Index);

        foreach (RedeemerInfo redeemer in sorted)
        {
            VmPlutusData? purpose = BuildScriptPurpose(
                redeemer.Tag, redeemer.Index,
                sortedInputs, sortedMint, certificates,
                sortedWithdrawals, sortedVoters, proposals);

            if (purpose is null)
            {
                continue;
            }

            VmPlutusData redeemerData = CodecPlutusDataToVm(CborSerializer.Deserialize<CodecPlutusData>(redeemer.DataCbor));
            entries.Add((purpose, redeemerData));
        }

        return Map(entries);
    }

    private static VmMap DataInfoToPlutusData(PostAlonzoTransactionWitnessSet witnessSet)
    {
        if (witnessSet.PlutusDataSet is null)
        {
            return EmptyMap();
        }

        List<(VmPlutusData Key, VmPlutusData Value)> entries = [];

        foreach (CodecPlutusData datum in witnessSet.PlutusDataSet.GetValue())
        {
            byte[] datumBytes = CborSerializer.Serialize(datum);
            byte[] hash = HashUtil.Blake2b256(datumBytes);
            entries.Add((Bytes(hash), CodecPlutusDataToVm(datum)));
        }

        // Sort by datum hash
        entries.Sort((a, b) =>
        {
            VmBytes aKey = (VmBytes)a.Key;
            VmBytes bKey = (VmBytes)b.Key;
            return aKey.Value.Span.SequenceCompareTo(bKey.Value.Span);
        });

        return Map(entries);
    }

    // ────────────────────── Votes Info ──────────────────────

    private static VmMap VotesToPlutusData(VotingProcedures? votingProcedures)
    {
        if (votingProcedures?.Value is null || votingProcedures.Value.Count == 0)
        {
            return EmptyMap();
        }

        List<(VmPlutusData Key, VmPlutusData Value)> entries = [];

        // Sort voters by (sort_index, hash) — port of Aiken sort_voters
        foreach ((Voter voter, GovActionIdVotingProcedure actions) in
            votingProcedures.Value
                .OrderBy(kv => VoterSortIndex(kv.Key))
                .ThenBy(kv => kv.Key.Hash, ByteMemoryComparer.Instance))
        {
            List<(VmPlutusData Key, VmPlutusData Value)> actionEntries = [];

            // Sort actions by (tx_id, action_index) — port of Aiken sort_gov_action_id
            foreach ((GovActionId actionId, VotingProcedure procedure) in
                actions.Value
                    .OrderBy(kv => kv.Key.TransactionId, ByteMemoryComparer.Instance)
                    .ThenBy(kv => kv.Key.GovActionIndex))
            {
                actionEntries.Add((GovActionIdToPlutusData(actionId), VoteToPlutusData(procedure)));
            }
            entries.Add((VoterToPlutusData(voter), Map(actionEntries)));
        }

        return Map(entries);
    }

    /// <summary>
    /// Voter sort index matching Aiken's sort_voters:
    /// CC script=0, CC key=1, DRep script=2, DRep key=3, SPO key=4.
    /// CDDL tags: 0=CC key, 1=CC script, 2=DRep key, 3=DRep script, 4=SPO key.
    /// </summary>
    private static int VoterSortIndex(Voter voter) => voter.Tag switch
    {
        1 => 0, // CC script
        0 => 1, // CC key
        3 => 2, // DRep script
        2 => 3, // DRep key
        4 => 4, // SPO key
        _ => 5
    };

    // ────────────────────── TxInfo V3 ──────────────────────

    /// <summary>
    /// Builds TxInfoV3 as VM PlutusData from a Conway transaction body.
    /// Port of Aiken TxInfoV3::from_transaction.
    /// </summary>
    public static VmPlutusData BuildTxInfoV3(
        ConwayTransactionBody body,
        PostAlonzoTransactionWitnessSet witnessSet,
        IReadOnlyList<ResolvedInput> utxos,
        SlotNetworkConfig slotConfig)
    {
        ArgumentNullException.ThrowIfNull(utxos);
        ArgumentNullException.ThrowIfNull(slotConfig);

        // Sort and resolve inputs
        List<(TransactionInput Input, ITransactionOutput Output)> sortedInputs =
            SortAndResolveInputs(body.Inputs.GetValue(), utxos);

        // Reference inputs
        List<(TransactionInput Input, ITransactionOutput Output)> referenceInputs =
            body.ReferenceInputs is not null
                ? SortAndResolveInputs(body.ReferenceInputs.GetValue(), utxos)
                : [];

        // Sorted mint
        List<(ReadOnlyMemory<byte> PolicyId, TokenBundleMint Bundle)>? sortedMint =
            body.Mint?.Value?
                .OrderBy(kv => kv.Key, ByteMemoryComparer.Instance)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

        // Certificates
        List<ICertificate> certificates = body.Certificates?.GetValue().ToList() ?? [];

        // Withdrawals — sorted by parsed address (network, credential type, hash) like Aiken
        List<(RewardAccount Account, ulong Coin)> sortedWithdrawals =
            body.Withdrawals?.Value?
                .OrderBy(kv => kv.Key.Value, RewardAccountComparer.Instance)
                .Select(kv => (kv.Key, kv.Value))
                .ToList() ?? [];

        // Proposals
        List<ProposalProcedure> proposals = body.ProposalProcedures?.GetValue().ToList() ?? [];

        // Voters — sorted by (sort_index, hash) like Aiken sort_voters
        List<Voter> sortedVoters = body.VotingProcedures?.Value?.Keys
            .OrderBy(VoterSortIndex)
            .ThenBy(v => v.Hash, ByteMemoryComparer.Instance)
            .ToList() ?? [];

        // Extract redeemers
        List<RedeemerInfo> redeemers = ExtractRedeemers(witnessSet.Redeemers);

        // Build TxId (hash of serialized body)
        byte[] txBodyBytes = CborSerializer.Serialize(body);
        byte[] txId = HashUtil.Blake2b256(txBodyBytes);

        // TxInfoV3 = Constr 0 [inputs, ref_inputs, outputs, fee, mint, certs, withdrawals,
        //                      valid_range, signatories, redeemers, data, tx_id, votes,
        //                      proposals, treasury_amount, treasury_donation]
        return Constr(0,
            // inputs
            List(sortedInputs.Select(i => TxInInfoToPlutusData(i.Input, i.Output))),
            // reference_inputs
            List(referenceInputs.Select(i => TxInInfoToPlutusData(i.Input, i.Output))),
            // outputs
            List(body.Outputs.GetValue().Select(OutputToPlutusData)),
            // fee
            Int(body.Fee),
            // mint
            MintToPlutusData(body.Mint),
            // certificates
            List(certificates.Select(CertificateToPlutusData)),
            // withdrawals
            WithdrawalsToPlutusData(body.Withdrawals),
            // valid_range
            TimeRangeToPlutusData(body.ValidityIntervalStart, body.TimeToLive, slotConfig),
            // signatories
            List(body.RequiredSigners?.GetValue()
                .OrderBy(s => s, ByteMemoryComparer.Instance)
                .Select(s => (VmPlutusData)Bytes(s)) ?? []),
            // redeemers (map of purpose → redeemer data)
            RedeemersToPlutusData(redeemers, sortedInputs, sortedMint, certificates,
                sortedWithdrawals, sortedVoters, proposals),
            // data (map of datum_hash → datum)
            DataInfoToPlutusData(witnessSet),
            // tx_id
            Bytes(txId),
            // votes
            VotesToPlutusData(body.VotingProcedures),
            // proposal_procedures
            List(proposals.Select(ProposalProcedureToPlutusData)),
            // current_treasury_amount
            body.TreasuryValue.HasValue ? OptionSome(Int(body.TreasuryValue.Value)) : OptionNone(),
            // treasury_donation
            body.Donation.HasValue ? OptionSome(Int(body.Donation.Value)) : OptionNone()
        );
    }

    // ────────────────────── IScript Context V3 ──────────────────────

    /// <summary>
    /// Builds a V3 ScriptContext for a specific redeemer.
    /// ScriptContext = Constr 0 [tx_info, redeemer_data, script_info]
    /// </summary>
    public static VmPlutusData BuildScriptContextV3(
        VmPlutusData txInfo,
        RedeemerInfo redeemer,
        List<(TransactionInput Input, ITransactionOutput Output)> sortedInputs,
        List<(ReadOnlyMemory<byte> PolicyId, TokenBundleMint Bundle)>? sortedMint,
        List<ICertificate> certificates,
        List<(RewardAccount Account, ulong Coin)> sortedWithdrawals,
        List<Voter> sortedVoters,
        List<ProposalProcedure> proposals,
        DataLookupTable lookupTable)
    {
        ArgumentNullException.ThrowIfNull(txInfo);
        ArgumentNullException.ThrowIfNull(redeemer);
        ArgumentNullException.ThrowIfNull(sortedInputs);
        ArgumentNullException.ThrowIfNull(certificates);
        ArgumentNullException.ThrowIfNull(sortedWithdrawals);
        ArgumentNullException.ThrowIfNull(sortedVoters);
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(lookupTable);

        VmPlutusData? scriptInfo = BuildScriptInfo(
            redeemer.Tag, redeemer.Index,
            sortedInputs, sortedMint, certificates,
            sortedWithdrawals, sortedVoters, proposals,
            lookupTable) ?? throw new InvalidOperationException(
                $"Could not build ScriptInfo for redeemer tag={redeemer.Tag} index={redeemer.Index}");
        VmPlutusData redeemerData = CodecPlutusDataToVm(CborSerializer.Deserialize<CodecPlutusData>(redeemer.DataCbor));

        return Constr(0, txInfo, redeemerData, scriptInfo);
    }

    // ────────────────────── Find IScript ──────────────────────

    /// <summary>
    /// Finds the script bytes and optional datum for a redeemer.
    /// Port of Aiken find_script.
    /// </summary>
    public static (byte[] ScriptBytes, int Version, VmPlutusData? Datum) FindScript(
        RedeemerInfo redeemer,
        ConwayTransactionBody body,
        IReadOnlyList<ResolvedInput> utxos,
        DataLookupTable lookupTable)
    {
        ArgumentNullException.ThrowIfNull(redeemer);
        ArgumentNullException.ThrowIfNull(utxos);
        ArgumentNullException.ThrowIfNull(lookupTable);

        List<(TransactionInput Input, ITransactionOutput Output)> sortedInputs =
            SortAndResolveInputs(body.Inputs.GetValue(), utxos);

        List<(ReadOnlyMemory<byte> PolicyId, TokenBundleMint Bundle)>? sortedMint =
            body.Mint?.Value?
                .OrderBy(kv => kv.Key, ByteMemoryComparer.Instance)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

        switch (redeemer.Tag)
        {
            case 0: // Spend
                {
                    int idx = (int)redeemer.Index;
                    if (idx >= sortedInputs.Count)
                    {
                        throw new InvalidOperationException("Spend redeemer index out of range");
                    }

                    (TransactionInput input, ITransactionOutput output) = sortedInputs[idx];
                    ReadOnlyMemory<byte> addressBytes = output switch
                    {
                        PostAlonzoTransactionOutput p => p.Address.Value,
                        AlonzoTransactionOutput a => a.Address.Value,
                        _ => throw new InvalidOperationException("Unknown output type")
                    };

                    byte[]? scriptHash = GetPaymentScriptHash(addressBytes) ?? throw new InvalidOperationException("Spend input does not have a script payment credential");
                    (byte[] scriptBytes, int version)? script = lookupTable.GetScript(scriptHash) ?? throw new InvalidOperationException($"IScript not found: {Convert.ToHexString(scriptHash)}");

                    // Resolve datum
                    VmPlutusData? datum = null;
                    if (output is PostAlonzoTransactionOutput postAlonzo)
                    {
                        datum = postAlonzo.Datum switch
                        {
                            DatumHashOption dh => ResolveDatum(dh.DatumHash, lookupTable),
                            InlineDatumOption inline => CodecPlutusDataToVm(
                                inline.Data.Deserialize<CodecPlutusData>()),
                            _ => null
                        };
                    }
                    else if (output is AlonzoTransactionOutput alonzo && alonzo.DatumHash is not null)
                    {
                        datum = ResolveDatum(alonzo.DatumHash.Value, lookupTable);
                    }

                    return (script.Value.scriptBytes, script.Value.version, datum);
                }

            case 1: // Mint
                {
                    int idx = (int)redeemer.Index;
                    if (sortedMint is null || idx >= sortedMint.Count)
                    {
                        throw new InvalidOperationException("Mint redeemer index out of range");
                    }

                    byte[] policyId = sortedMint[idx].PolicyId.ToArray();
                    (byte[] scriptBytes, int version)? script = lookupTable.GetScript(policyId) ?? throw new InvalidOperationException($"Minting script not found: {Convert.ToHexString(policyId)}");
                    return (script.Value.scriptBytes, script.Value.version, null);
                }

            case 3: // Reward
                {
                    List<(RewardAccount Account, ulong Coin)> sortedWithdrawals =
                        body.Withdrawals?.Value?
                            .OrderBy(kv => kv.Key.Value, RewardAccountComparer.Instance)
                            .Select(kv => (kv.Key, kv.Value))
                            .ToList() ?? [];

                    int idx = (int)redeemer.Index;
                    if (idx >= sortedWithdrawals.Count)
                    {
                        throw new InvalidOperationException("Reward redeemer index out of range");
                    }

                    ReadOnlySpan<byte> rewardBytes = sortedWithdrawals[idx].Account.Value.Span;
                    if (rewardBytes.Length < 29)
                    {
                        throw new InvalidOperationException("Invalid reward account");
                    }

                    bool isScript = (rewardBytes[0] & 0x10) != 0;
                    if (!isScript)
                    {
                        throw new InvalidOperationException("Non-script withdrawal");
                    }

                    byte[] hash = rewardBytes.Slice(1, 28).ToArray();
                    (byte[] scriptBytes, int version)? script = lookupTable.GetScript(hash) ?? throw new InvalidOperationException($"Withdrawal script not found: {Convert.ToHexString(hash)}");
                    return (script.Value.scriptBytes, script.Value.version, null);
                }

            case 2: // Cert
                {
                    List<ICertificate> certificates = body.Certificates?.GetValue().ToList() ?? [];
                    int idx = (int)redeemer.Index;
                    if (idx >= certificates.Count)
                    {
                        throw new InvalidOperationException("Cert redeemer index out of range");
                    }

                    ICertificate cert = certificates[idx];
                    byte[]? hash = GetCertificateScriptHash(cert) ?? throw new InvalidOperationException("ICertificate does not have a script credential");
                    (byte[] scriptBytes, int version)? script = lookupTable.GetScript(hash) ?? throw new InvalidOperationException($"ICertificate script not found: {Convert.ToHexString(hash)}");
                    return (script.Value.scriptBytes, script.Value.version, null);
                }

            default:
                throw new InvalidOperationException($"Unsupported redeemer tag: {redeemer.Tag}");
        }
    }

    private static byte[]? GetCertificateScriptHash(ICertificate cert)
    {
        Credential? credential = cert switch
        {
            StakeDeregistration d => d.StakeCredential,
            UnRegCert u => u.StakeCredential,
            StakeDelegation d => d.StakeCredential,
            VoteDelegCert v => v.StakeCredential,
            StakeVoteDelegCert sv => sv.StakeCredential,
            StakeRegDelegCert sr => sr.StakeCredential,
            VoteRegDelegCert vr => vr.StakeCredential,
            StakeVoteRegDelegCert svr => svr.StakeCredential,
            RegDrepCert r => r.DRepCredential,
            UnRegDrepCert u => u.DRepCredential,
            UpdateDrepCert u => u.DRepCredential,
            AuthCommitteeHotCert a => a.ColdCredential,
            _ => null
        };

        if (credential is null)
        {
            return null;
        }

        // CredentialType 1 = ScriptHash
        return credential.Value.Type == 1 ? credential.Value.Hash.ToArray() : null;
    }

    // ────────────────────── Redeemer Extraction ──────────────────────

    /// <inheritdoc/>
    public static List<RedeemerInfo> ExtractRedeemers(IRedeemers? redeemers) => redeemers is null
            ? []
            : redeemers switch
            {
                RedeemerList list => [.. list.Value.Select(e => new RedeemerInfo(
                    e.Tag, e.Index,
                    CborSerializer.Serialize(e.Data),
                    e.ExUnits.Mem, e.ExUnits.Steps))],

                RedeemerMap map => [.. map.Value.Select(kv => new RedeemerInfo(
                    kv.Key.Tag, kv.Key.Index,
                    CborSerializer.Serialize(kv.Value.Data),
                    kv.Value.ExUnits.Mem, kv.Value.ExUnits.Steps))],

                _ => []
            };

    // ────────────────────── Top-Level: Evaluate All Redeemers ──────────────────────

    /// <summary>
    /// Evaluates all redeemers in a transaction. Returns updated evaluation results.
    /// Port of Aiken eval_phase_two.
    /// </summary>
    public static IReadOnlyList<Plutus.VM.Models.EvaluationResult> EvaluateTx(
        ConwayTransactionBody body,
        PostAlonzoTransactionWitnessSet witnessSet,
        IReadOnlyList<ResolvedInput> utxos,
        SlotNetworkConfig slotConfig)
    {
        ArgumentNullException.ThrowIfNull(utxos);
        ArgumentNullException.ThrowIfNull(slotConfig);

        List<RedeemerInfo> redeemers = ExtractRedeemers(witnessSet.Redeemers);
        if (redeemers.Count == 0)
        {
            return [];
        }

        DataLookupTable lookupTable = DataLookupTable.FromTransaction(witnessSet, utxos);

        // Pre-compute sorted collections (shared across all redeemers)
        List<(TransactionInput Input, ITransactionOutput Output)> sortedInputs =
            SortAndResolveInputs(body.Inputs.GetValue(), utxos);

        List<(ReadOnlyMemory<byte> PolicyId, TokenBundleMint Bundle)>? sortedMint =
            body.Mint?.Value?
                .OrderBy(kv => kv.Key, ByteMemoryComparer.Instance)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

        List<ICertificate> certificates = body.Certificates?.GetValue().ToList() ?? [];

        List<(RewardAccount Account, ulong Coin)> sortedWithdrawals =
            body.Withdrawals?.Value?
                .OrderBy(kv => kv.Key.Value, RewardAccountComparer.Instance)
                .Select(kv => (kv.Key, kv.Value))
                .ToList() ?? [];

        List<ProposalProcedure> proposals = body.ProposalProcedures?.GetValue().ToList() ?? [];
        List<Voter> sortedVoters = body.VotingProcedures?.Value?.Keys
            .OrderBy(VoterSortIndex)
            .ThenBy(v => v.Hash, ByteMemoryComparer.Instance)
            .ToList() ?? [];

        // Build TxInfo once (shared across all redeemers)
        VmPlutusData txInfo = BuildTxInfoV3(body, witnessSet, utxos, slotConfig);

        List<Plutus.VM.Models.EvaluationResult> results = [];

        foreach (RedeemerInfo redeemer in redeemers)
        {
            // Find script
            (byte[] scriptBytes, int version, VmPlutusData? datum) =
                FindScript(redeemer, body, utxos, lookupTable);

            // Build ScriptContext
            VmPlutusData scriptContext = BuildScriptContextV3(
                txInfo, redeemer,
                sortedInputs, sortedMint, certificates,
                sortedWithdrawals, sortedVoters, proposals,
                lookupTable);

            // Apply arguments based on version
            // V3: script(script_context)
            // V1/V2: script(datum?, redeemer, script_context)
            List<VmPlutusData> arguments = [];
            if (version is 1 or 2)
            {
                if (datum is not null)
                {
                    arguments.Add(datum);
                }

                VmPlutusData redeemerData = CodecPlutusDataToVm(CborSerializer.Deserialize<CodecPlutusData>(redeemer.DataCbor));
                arguments.Add(redeemerData);
            }
            arguments.Add(scriptContext);

            // Unwrap CBOR bytestring envelope to get raw flat-encoded bytes
            // Cardano scripts are double-CBOR-wrapped: outer bytestring → inner bytestring → flat bytes
            byte[] flatBytes = UnwrapCborByteString(scriptBytes);

            // Evaluate
            Plutus.VM.Models.EvaluationResult result =
                Plutus.VM.EvalTx.Evaluator.EvaluateScript(flatBytes, arguments);

            results.Add(new Plutus.VM.Models.EvaluationResult(
                redeemer.Tag,
                redeemer.Index,
                result.ExUnits));
        }

        return results;
    }

    /// <summary>
    /// Unwraps a CBOR bytestring envelope. Cardano Plutus scripts in the witness set
    /// are stored as CBOR bytestrings wrapping the flat-encoded program bytes.
    /// </summary>
    private static byte[] UnwrapCborByteString(byte[] data)
    {
        SAIB.Cbor.Serialization.CborReader reader = new(data);
        return reader.ReadByteStringToArray();
    }
}

/// <summary>
/// Collects scripts and datums from the witness set and UTxO script refs.
/// Port of Aiken DataLookupTable.
/// </summary>
public sealed class DataLookupTable
{
    private readonly Dictionary<string, (byte[] ScriptBytes, int Version)> _scripts = [];
    private readonly Dictionary<string, byte[]> _datums = [];

    /// <summary>
    /// Builds a lookup table from the transaction witness set and resolved UTxOs.
    /// </summary>
    public static DataLookupTable FromTransaction(
        PostAlonzoTransactionWitnessSet witnessSet,
        IReadOnlyList<ResolvedInput> utxos)
    {
        ArgumentNullException.ThrowIfNull(utxos);

        DataLookupTable table = new();

        // Witness set scripts — hash is blake2b-224(version_byte || script_cbor)
        if (witnessSet.PlutusV1Scripts is not null)
        {
            foreach (ReadOnlyMemory<byte> script in witnessSet.PlutusV1Scripts.GetValue())
            {
                byte[] hash = ScriptHash(1, script.Span);
                table._scripts[Convert.ToHexString(hash)] = (script.ToArray(), 1);
            }
        }

        if (witnessSet.PlutusV2Scripts is not null)
        {
            foreach (ReadOnlyMemory<byte> script in witnessSet.PlutusV2Scripts.GetValue())
            {
                byte[] hash = ScriptHash(2, script.Span);
                table._scripts[Convert.ToHexString(hash)] = (script.ToArray(), 2);
            }
        }

        if (witnessSet.PlutusV3Scripts is not null)
        {
            foreach (ReadOnlyMemory<byte> script in witnessSet.PlutusV3Scripts.GetValue())
            {
                byte[] hash = ScriptHash(3, script.Span);
                table._scripts[Convert.ToHexString(hash)] = (script.ToArray(), 3);
            }
        }

        // Witness set datums
        if (witnessSet.PlutusDataSet is not null)
        {
            foreach (CodecPlutusData datum in witnessSet.PlutusDataSet.GetValue())
            {
                byte[] datumBytes = CborSerializer.Serialize(datum);
                byte[] hash = HashUtil.Blake2b256(datumBytes);
                table._datums[Convert.ToHexString(hash)] = datumBytes;
            }
        }

        // UTxO script refs — unwrap tag 24 → parse [language, script_bytes]
        foreach (ResolvedInput utxo in utxos)
        {
            if (utxo.Output is PostAlonzoTransactionOutput postAlonzo && postAlonzo.ScriptRef is not null)
            {
                // ScriptRef = #6.24(bytes .cbor [language, script_bytes])
                // Unwrap tag 24 + bytestring header to get inner CBOR: array [lang, script]
                byte[] rawInner = postAlonzo.ScriptRef.GetValue();
                if (rawInner.Length > 2 && rawInner[0] == 0x82) // definite array of 2
                {
                    int language = rawInner[1]; // single-byte CBOR unsigned int 0-23
                    // Parse the script bytestring after the language byte
                    SAIB.Cbor.Serialization.CborReader reader = new(rawInner.AsSpan(2));
                    byte[] scriptBytes = reader.ReadByteStringToArray();

                    // Plutus script version: 1=V1, 2=V2, 3=V3 (language 0 = NativeScript, skip)
                    if (language is >= 1 and <= 3)
                    {
                        byte[] hash = ScriptHash(language, scriptBytes);
                        table._scripts[Convert.ToHexString(hash)] = (scriptBytes, language);
                    }
                }
            }
        }

        return table;
    }

    /// <summary>
    /// Gets a script by its hash.
    /// </summary>
    public (byte[] ScriptBytes, int Version)? GetScript(byte[] scriptHash)
    {
        string key = Convert.ToHexString(scriptHash);
        return _scripts.TryGetValue(key, out (byte[] ScriptBytes, int Version) script) ? script : null;
    }

    /// <summary>
    /// Gets a datum by its hash.
    /// </summary>
    public byte[]? GetDatum(byte[] datumHash)
    {
        string key = Convert.ToHexString(datumHash);
        return _datums.TryGetValue(key, out byte[]? datum) ? datum : null;
    }

    /// <summary>
    /// Computes a Cardano script hash: blake2b-224(version_byte || script_bytes).
    /// </summary>
    private static byte[] ScriptHash(int version, ReadOnlySpan<byte> scriptBytes)
    {
        byte[] prefixed = new byte[1 + scriptBytes.Length];
        prefixed[0] = (byte)version;
        scriptBytes.CopyTo(prefixed.AsSpan(1));
        return HashUtil.Blake2b224(prefixed);
    }
}

/// <summary>
/// Redeemer tags: 0=Spend, 1=Mint, 2=Cert, 3=Reward, 4=Vote, 5=Propose
/// </summary>
public sealed record RedeemerInfo(int Tag, ulong Index, byte[] DataCbor, ulong ExUnitsMem, ulong ExUnitsSteps);

/// <summary>
/// Comparer for ReadOnlyMemory&lt;byte&gt; that does lexicographic byte comparison.
/// Used for sorting (IComparer) in ScriptContext building.
/// </summary>
internal sealed class ByteMemoryComparer : IComparer<ReadOnlyMemory<byte>>
{
    public static readonly ByteMemoryComparer Instance = new();

    public int Compare(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y) => x.Span.SequenceCompareTo(y.Span);
}

/// <summary>
/// Comparer for reward account addresses matching Aiken's sort_reward_accounts:
/// 1. Compare network tag (testnet=0 &lt; mainnet=1)
/// 2. Compare credential type (script &lt; key)
/// 3. Compare credential hash bytes
/// </summary>
internal sealed class RewardAccountComparer : IComparer<ReadOnlyMemory<byte>>
{
    public static readonly RewardAccountComparer Instance = new();

    public int Compare(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y)
    {
        ReadOnlySpan<byte> a = x.Span;
        ReadOnlySpan<byte> b = y.Span;

        if (a.Length < 29 || b.Length < 29)
        {
            return a.SequenceCompareTo(b);
        }

        // Network is lower 4 bits of header byte
        int networkA = a[0] & 0x0F;
        int networkB = b[0] & 0x0F;
        if (networkA != networkB)
        {
            return networkA.CompareTo(networkB);
        }

        // Credential type: bit 4 of header — 1=script, 0=key
        // Aiken: script < key (script sorts first)
        bool isScriptA = (a[0] & 0x10) != 0;
        bool isScriptB = (b[0] & 0x10) != 0;
        if (isScriptA != isScriptB)
        {
            return isScriptA ? -1 : 1;
        }

        // Same type: compare hash bytes (1..29)
        return a.Slice(1, 28).SequenceCompareTo(b.Slice(1, 28));
    }
}
