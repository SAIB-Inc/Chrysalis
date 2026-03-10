using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Tx.Utils;

/// <summary>
/// Factory for constructing V2 Codec readonly record struct types from values.
/// Uses CBOR serialization round-trip: mirror record → serialize → deserialize as target struct.
/// </summary>
public static class CborFactory
{
    // ──────────── Value Types ────────────

    /// <summary>Creates a <see cref="Lovelace"/> value.</summary>
    public static Lovelace CreateLovelace(ulong amount)
        => Roundtrip<LovelaceMirror, Lovelace>(new(amount));

    /// <summary>Creates a <see cref="LovelaceWithMultiAsset"/> value.</summary>
    public static LovelaceWithMultiAsset CreateLovelaceWithMultiAsset(ulong amount, MultiAssetOutput multiAsset)
        => Roundtrip<LovelaceWithMultiAssetMirror, LovelaceWithMultiAsset>(new(amount, multiAsset));

    // ──────────── Transaction I/O ────────────

    /// <summary>Creates a <see cref="TransactionInput"/>.</summary>
    public static TransactionInput CreateTransactionInput(ReadOnlyMemory<byte> txId, ulong index)
        => Roundtrip<TransactionInputMirror, TransactionInput>(new(txId, index));

    /// <summary>Creates an <see cref="AlonzoTransactionOutput"/>.</summary>
    public static AlonzoTransactionOutput CreateAlonzoTransactionOutput(
        Address address, IValue amount, ReadOnlyMemory<byte>? datumHash)
        => Roundtrip<AlonzoTxOutMirror, AlonzoTransactionOutput>(new(address, amount, datumHash));

    /// <summary>Creates a <see cref="PostAlonzoTransactionOutput"/>.</summary>
    public static PostAlonzoTransactionOutput CreatePostAlonzoTransactionOutput(
        Address address, IValue amount, IDatumOption? datum, CborEncodedValue? scriptRef)
        => Roundtrip<PostAlonzoTxOutMirror, PostAlonzoTransactionOutput>(new(address, amount, datum, scriptRef));

    // ──────────── Protocol Types ────────────

    /// <summary>Creates an <see cref="ExUnits"/>.</summary>
    public static ExUnits CreateExUnits(ulong mem, ulong steps)
        => Roundtrip<ExUnitsMirror, ExUnits>(new(mem, steps));

    /// <summary>Creates an <see cref="ExUnitPrices"/>.</summary>
    public static ExUnitPrices CreateExUnitPrices(CborRationalNumber memPrice, CborRationalNumber stepPrice)
        => Roundtrip<ExUnitPricesMirror, ExUnitPrices>(new(memPrice, stepPrice));

    /// <summary>Creates a <see cref="ProtocolVersion"/>.</summary>
    public static ProtocolVersion CreateProtocolVersion(ulong major, ulong sequenceNumber)
        => Roundtrip<ProtocolVersionMirror, ProtocolVersion>(new(major, sequenceNumber));

    /// <summary>Creates a <see cref="CborRationalNumber"/>.</summary>
    public static CborRationalNumber CreateRationalNumber(ulong numerator, ulong denominator)
        => Roundtrip<CborRationalNumberMirror, CborRationalNumber>(new(numerator, denominator));

    /// <summary>Creates a <see cref="PoolVotingThresholds"/>.</summary>
    public static PoolVotingThresholds CreatePoolVotingThresholds(
        CborRationalNumber motionNoConfidence,
        CborRationalNumber committeeNormal,
        CborRationalNumber committeeNoConfidence,
        CborRationalNumber hardForkInitiation,
        CborRationalNumber securityRelevantThreshold)
        => Roundtrip<PoolVotingThresholdsMirror, PoolVotingThresholds>(
            new(motionNoConfidence, committeeNormal, committeeNoConfidence,
                hardForkInitiation, securityRelevantThreshold));

    /// <summary>Creates a <see cref="DRepVotingThresholds"/>.</summary>
    public static DRepVotingThresholds CreateDRepVotingThresholds(
        CborRationalNumber motionNoConfidence,
        CborRationalNumber committeeNormal,
        CborRationalNumber committeeNoConfidence,
        CborRationalNumber updateConstitution,
        CborRationalNumber hardForkInitiation,
        CborRationalNumber ppNetworkGroup,
        CborRationalNumber ppEconomicGroup,
        CborRationalNumber ppTechnicalGroup,
        CborRationalNumber ppGovernanceGroup,
        CborRationalNumber treasuryWithdrawal)
        => Roundtrip<DRepVotingThresholdsMirror, DRepVotingThresholds>(
            new(motionNoConfidence, committeeNormal, committeeNoConfidence,
                updateConstitution, hardForkInitiation, ppNetworkGroup,
                ppEconomicGroup, ppTechnicalGroup, ppGovernanceGroup, treasuryWithdrawal));

    // ──────────── Witness Types ────────────

    /// <summary>Creates a <see cref="VKeyWitness"/>.</summary>
    public static VKeyWitness CreateVKeyWitness(ReadOnlyMemory<byte> vKey, ReadOnlyMemory<byte> signature)
        => Roundtrip<VKeyWitnessMirror, VKeyWitness>(new(vKey, signature));

    // ──────────── Script Types ────────────

    /// <summary>Creates a <see cref="PlutusV1Script"/>.</summary>
    public static PlutusV1Script CreatePlutusV1Script(int tag, ReadOnlyMemory<byte> scriptBytes)
        => Roundtrip<PlutusV1ScriptMirror, PlutusV1Script>(new(tag, scriptBytes));

    /// <summary>Creates a <see cref="PlutusV2Script"/>.</summary>
    public static PlutusV2Script CreatePlutusV2Script(int tag, ReadOnlyMemory<byte> scriptBytes)
        => Roundtrip<PlutusV2ScriptMirror, PlutusV2Script>(new(tag, scriptBytes));

    /// <summary>Creates a <see cref="PlutusV3Script"/>.</summary>
    public static PlutusV3Script CreatePlutusV3Script(int tag, ReadOnlyMemory<byte> scriptBytes)
        => Roundtrip<PlutusV3ScriptMirror, PlutusV3Script>(new(tag, scriptBytes));

    /// <summary>Creates a <see cref="MultiSigScript"/>.</summary>
    public static MultiSigScript CreateMultiSigScript(int tag, INativeScript nativeScript)
        => Roundtrip<MultiSigScriptMirror, MultiSigScript>(new(tag, nativeScript));

    // ──────────── Datum Options ────────────

    /// <summary>Creates an <see cref="InlineDatumOption"/>.</summary>
    public static InlineDatumOption CreateInlineDatumOption(int tag, CborEncodedValue data)
        => Roundtrip<InlineDatumOptionMirror, InlineDatumOption>(new(tag, data));

    /// <summary>Creates a <see cref="DatumHashOption"/>.</summary>
    public static DatumHashOption CreateDatumHashOption(int tag, ReadOnlyMemory<byte> datumHash)
        => Roundtrip<DatumHashOptionMirror, DatumHashOption>(new(tag, datumHash));

    // ──────────── Redeemer Types ────────────

    /// <summary>Creates a <see cref="RedeemerEntry"/>.</summary>
    public static RedeemerEntry CreateRedeemerEntry(int tag, ulong index, IPlutusData data, ExUnits exUnits)
        => Roundtrip<RedeemerEntryMirror, RedeemerEntry>(new(tag, index, data, exUnits));

    /// <summary>Creates a <see cref="RedeemerKey"/>.</summary>
    public static RedeemerKey CreateRedeemerKey(int tag, ulong index)
        => Roundtrip<RedeemerKeyMirror, RedeemerKey>(new(tag, index));

    /// <summary>Creates a <see cref="RedeemerValue"/>.</summary>
    public static RedeemerValue CreateRedeemerValue(IPlutusData data, ExUnits exUnits)
        => Roundtrip<RedeemerValueMirror, RedeemerValue>(new(data, exUnits));

    /// <summary>Creates a <see cref="RedeemerList"/>.</summary>
    public static RedeemerList CreateRedeemerList(List<RedeemerEntry> entries)
        => Roundtrip<RedeemerListMirror, RedeemerList>(new(entries));

    /// <summary>Creates a <see cref="RedeemerMap"/>.</summary>
    public static RedeemerMap CreateRedeemerMap(Dictionary<RedeemerKey, RedeemerValue> entries)
        => Roundtrip<RedeemerMapMirror, RedeemerMap>(new(entries));

    // ──────────── Collection Wrappers ────────────

    /// <summary>Creates a <see cref="TokenBundleOutput"/>.</summary>
    public static TokenBundleOutput CreateTokenBundleOutput(Dictionary<ReadOnlyMemory<byte>, ulong> value)
        => Roundtrip<TokenBundleOutputMirror, TokenBundleOutput>(new(value));

    /// <summary>Creates a <see cref="TokenBundleMint"/>.</summary>
    public static TokenBundleMint CreateTokenBundleMint(Dictionary<ReadOnlyMemory<byte>, long> value)
        => Roundtrip<TokenBundleMintMirror, TokenBundleMint>(new(value));

    /// <summary>Creates a <see cref="MultiAssetOutput"/>.</summary>
    public static MultiAssetOutput CreateMultiAssetOutput(Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> value)
        => Roundtrip<MultiAssetOutputMirror, MultiAssetOutput>(new(value));

    /// <summary>Creates a <see cref="MultiAssetMint"/>.</summary>
    public static MultiAssetMint CreateMultiAssetMint(Dictionary<ReadOnlyMemory<byte>, TokenBundleMint> value)
        => Roundtrip<MultiAssetMintMirror, MultiAssetMint>(new(value));

    /// <summary>Creates a <see cref="CborDefList{T}"/>.</summary>
    public static CborDefList<T> CreateDefList<T>(List<T> items) where T : ICborType
        => Roundtrip<CborDefListMirror<T>, CborDefList<T>>(new(items));

    /// <summary>Creates a <see cref="CborDefListWithTag{T}"/>.</summary>
    public static CborDefListWithTag<T> CreateDefListWithTag<T>(List<T> items) where T : ICborType
        => Roundtrip<CborDefListWithTagMirror<T>, CborDefListWithTag<T>>(new(items));

    /// <summary>Creates a <see cref="CborIndefList{T}"/>.</summary>
    public static CborIndefList<T> CreateIndefList<T>(List<T> items) where T : ICborType
        => Roundtrip<CborIndefListMirror<T>, CborIndefList<T>>(new(items));

    /// <summary>Creates a <see cref="CborDefList{T}"/> for long values.</summary>
    public static CborDefList<long> CreateDefListLong(List<long> items)
        => Roundtrip<CborDefListLongMirror, CborDefList<long>>(new(items));

    /// <summary>Creates a <see cref="CborDefListWithTag{T}"/> for byte arrays.</summary>
    public static CborDefListWithTag<ReadOnlyMemory<byte>> CreateDefListWithTagBytes(List<ReadOnlyMemory<byte>> items)
        => Roundtrip<CborDefListWithTagBytesMirror, CborDefListWithTag<ReadOnlyMemory<byte>>>(new(items));

    // ──────────── Metadata Types ────────────

    /// <summary>Creates a <see cref="MetadataText"/>.</summary>
    public static MetadataText CreateMetadataText(string value)
        => Roundtrip<MetadataTextMirror, MetadataText>(new(value));

    /// <summary>Creates a <see cref="MetadatumIntLong"/>.</summary>
    public static MetadatumIntLong CreateMetadatumIntLong(long value)
        => Roundtrip<MetadatumIntLongMirror, MetadatumIntLong>(new(value));

    /// <summary>Creates a <see cref="MetadatumIntUlong"/>.</summary>
    public static MetadatumIntUlong CreateMetadatumIntUlong(ulong value)
        => Roundtrip<MetadatumIntUlongMirror, MetadatumIntUlong>(new(value));

    /// <summary>Creates a <see cref="MetadatumMap"/>.</summary>
    public static MetadatumMap CreateMetadatumMap(Dictionary<ITransactionMetadatum, ITransactionMetadatum> value)
        => Roundtrip<MetadatumMapMirror, MetadatumMap>(new(value));

    /// <summary>Creates a <see cref="MetadatumList"/>.</summary>
    public static MetadatumList CreateMetadatumList(List<ITransactionMetadatum> value)
        => Roundtrip<MetadatumListMirror, MetadatumList>(new(value));

    /// <summary>Creates a <see cref="MetadatumBytes"/>.</summary>
    public static MetadatumBytes CreateMetadatumBytes(byte[] value)
        => Roundtrip<MetadatumBytesMirror, MetadatumBytes>(new(value));

    // ──────────── Transaction Structure ────────────

    /// <summary>Creates a <see cref="Credential"/>.</summary>
    public static Credential CreateCredential(int type, ReadOnlyMemory<byte> hash)
        => Roundtrip<CredentialMirror, Credential>(new(type, hash));

    /// <summary>Creates a <see cref="PostMaryTransaction"/>.</summary>
    public static PostMaryTransaction CreatePostMaryTransaction(
        ITransactionBody body, ITransactionWitnessSet witnesses,
        bool isValid, IAuxiliaryData? auxiliaryData)
        => Roundtrip<PostMaryTransactionMirror, PostMaryTransaction>(
            new(body, witnesses, isValid, auxiliaryData));

    /// <summary>Creates a <see cref="Metadata"/>.</summary>
    public static Metadata CreateMetadata(Dictionary<ulong, ITransactionMetadatum> value)
        => Roundtrip<MetadataMirror, Metadata>(new(value));

    // ──────────── Transaction Body (CborMap) ────────────

    /// <summary>Creates a <see cref="ConwayTransactionBody"/>.</summary>
    public static ConwayTransactionBody CreateConwayTransactionBody(
        ICborMaybeIndefList<TransactionInput> inputs,
        ICborMaybeIndefList<ITransactionOutput> outputs,
        ulong fee,
        ulong? timeToLive = null,
        ICborMaybeIndefList<ICertificate>? certificates = null,
        Withdrawals? withdrawals = null,
        ReadOnlyMemory<byte>? auxiliaryDataHash = null,
        ulong? validityIntervalStart = null,
        MultiAssetMint? mint = null,
        ReadOnlyMemory<byte>? scriptDataHash = null,
        ICborMaybeIndefList<TransactionInput>? collateral = null,
        ICborMaybeIndefList<ReadOnlyMemory<byte>>? requiredSigners = null,
        int? networkId = null,
        ITransactionOutput? collateralReturn = null,
        ulong? totalCollateral = null,
        ICborMaybeIndefList<TransactionInput>? referenceInputs = null,
        VotingProcedures? votingProcedures = null,
        ICborMaybeIndefList<ProposalProcedure>? proposalProcedures = null,
        ulong? treasuryValue = null,
        ulong? donation = null)
        => Roundtrip<ConwayBodyMirror, ConwayTransactionBody>(
            new(inputs, outputs, fee, timeToLive, certificates, withdrawals,
                auxiliaryDataHash, validityIntervalStart, mint, scriptDataHash,
                collateral, requiredSigners, networkId, collateralReturn,
                totalCollateral, referenceInputs, votingProcedures,
                proposalProcedures, treasuryValue, donation));

    // ──────────── Witness Set (CborMap) ────────────

    /// <summary>Creates a <see cref="PostAlonzoTransactionWitnessSet"/>.</summary>
    public static PostAlonzoTransactionWitnessSet CreateWitnessSet(
        ICborMaybeIndefList<VKeyWitness>? vKeyWitnesses = null,
        ICborMaybeIndefList<INativeScript>? nativeScripts = null,
        ICborMaybeIndefList<BootstrapWitness>? bootstrapWitnesses = null,
        ICborMaybeIndefList<ReadOnlyMemory<byte>>? plutusV1Scripts = null,
        ICborMaybeIndefList<IPlutusData>? plutusDataSet = null,
        IRedeemers? redeemers = null,
        ICborMaybeIndefList<ReadOnlyMemory<byte>>? plutusV2Scripts = null,
        ICborMaybeIndefList<ReadOnlyMemory<byte>>? plutusV3Scripts = null)
        => Roundtrip<WitnessSetMirror, PostAlonzoTransactionWitnessSet>(
            new(vKeyWitnesses, nativeScripts, bootstrapWitnesses,
                plutusV1Scripts, plutusDataSet, redeemers,
                plutusV2Scripts, plutusV3Scripts));

    // ──────────── Auxiliary Data (CborMap) ────────────

    /// <summary>Creates a <see cref="PostAlonzoAuxiliaryDataMap"/>.</summary>
    public static PostAlonzoAuxiliaryDataMap CreateAuxiliaryData(
        Metadata? transactionMetadata = null,
        ICborMaybeIndefList<INativeScript>? nativeScripts = null,
        ICborMaybeIndefList<ReadOnlyMemory<byte>>? plutusV1Scripts = null,
        ICborMaybeIndefList<ReadOnlyMemory<byte>>? plutusV2Scripts = null,
        ICborMaybeIndefList<ReadOnlyMemory<byte>>? plutusV3Scripts = null)
        => Roundtrip<AuxiliaryDataMirror, PostAlonzoAuxiliaryDataMap>(
            new(transactionMetadata, nativeScripts, plutusV1Scripts,
                plutusV2Scripts, plutusV3Scripts));

    // ──────────── Helper ────────────

    private static TTarget Roundtrip<TMirror, TTarget>(TMirror mirror)
        where TMirror : ICborType
        => CborSerializer.Deserialize<TTarget>(CborSerializer.Serialize(mirror));
}

// ══════════════════════════════════════════════════════════════════════
// Mirror types: partial record classes that produce the same CBOR
// as their corresponding V2 readonly record struct targets.
// ══════════════════════════════════════════════════════════════════════

// ──────────── Value Mirrors ────────────

/// <summary>Mirror for <see cref="Lovelace"/>.</summary>
[CborSerializable]
public partial record LovelaceMirror(ulong Amount) : CborRecord;

/// <summary>Mirror for <see cref="LovelaceWithMultiAsset"/>.</summary>
[CborSerializable]
[CborList]
public partial record LovelaceWithMultiAssetMirror(
    [CborOrder(0)] ulong Amount,
    [CborOrder(1)] MultiAssetOutput MultiAsset) : CborRecord;

// ──────────── Transaction I/O Mirrors ────────────

/// <summary>Mirror for <see cref="TransactionInput"/>.</summary>
[CborSerializable]
[CborList]
public partial record TransactionInputMirror(
    [CborOrder(0)] ReadOnlyMemory<byte> TransactionId,
    [CborOrder(1)] ulong Index) : CborRecord;

/// <summary>Mirror for <see cref="AlonzoTransactionOutput"/>.</summary>
[CborSerializable]
[CborList]
public partial record AlonzoTxOutMirror(
    [CborOrder(0)] Address Address,
    [CborOrder(1)] IValue Amount,
    [CborOrder(2)] ReadOnlyMemory<byte>? DatumHash) : CborRecord;

/// <summary>Mirror for <see cref="PostAlonzoTransactionOutput"/>.</summary>
[CborSerializable]
[CborMap]
public partial record PostAlonzoTxOutMirror(
    [CborProperty(0)] Address Address,
    [CborProperty(1)] IValue Amount,
    [CborProperty(2)] IDatumOption? Datum,
    [CborProperty(3)] CborEncodedValue? ScriptRef) : CborRecord;

// ──────────── Protocol Mirrors ────────────

/// <summary>Mirror for <see cref="ExUnits"/>.</summary>
[CborSerializable]
[CborList]
public partial record ExUnitsMirror(
    [CborOrder(0)] ulong Mem,
    [CborOrder(1)] ulong Steps) : CborRecord;

/// <summary>Mirror for <see cref="ExUnitPrices"/>.</summary>
[CborSerializable]
[CborList]
public partial record ExUnitPricesMirror(
    [CborOrder(0)] CborRationalNumber MemPrice,
    [CborOrder(1)] CborRationalNumber StepPrice) : CborRecord;

/// <summary>Mirror for <see cref="ProtocolVersion"/>.</summary>
[CborSerializable]
[CborList]
public partial record ProtocolVersionMirror(
    [CborOrder(0)] ulong Major,
    [CborOrder(1)] ulong SequenceNumber) : CborRecord;

/// <summary>Mirror for <see cref="CborRationalNumber"/>.</summary>
[CborSerializable]
[CborTag(30)]
[CborList]
public partial record CborRationalNumberMirror(
    [CborOrder(0)] ulong Numerator,
    [CborOrder(1)] ulong Denominator) : CborRecord;

/// <summary>Mirror for <see cref="PoolVotingThresholds"/>.</summary>
[CborSerializable]
[CborList]
public partial record PoolVotingThresholdsMirror(
    [CborOrder(0)] CborRationalNumber MotionNoConfidence,
    [CborOrder(1)] CborRationalNumber CommitteeNormal,
    [CborOrder(2)] CborRationalNumber CommitteeNoConfidence,
    [CborOrder(3)] CborRationalNumber HardForkInitiation,
    [CborOrder(4)] CborRationalNumber SecurityRelevantThreshold) : CborRecord;

/// <summary>Mirror for <see cref="DRepVotingThresholds"/>.</summary>
[CborSerializable]
[CborList]
public partial record DRepVotingThresholdsMirror(
    [CborOrder(0)] CborRationalNumber MotionNoConfidence,
    [CborOrder(1)] CborRationalNumber CommitteeNormal,
    [CborOrder(2)] CborRationalNumber CommitteeNoConfidence,
    [CborOrder(3)] CborRationalNumber UpdateConstitution,
    [CborOrder(4)] CborRationalNumber HardForkInitiation,
    [CborOrder(5)] CborRationalNumber PPNetworkGroup,
    [CborOrder(6)] CborRationalNumber PPEconomicGroup,
    [CborOrder(7)] CborRationalNumber PPTechnicalGroup,
    [CborOrder(8)] CborRationalNumber PPGovernanceGroup,
    [CborOrder(9)] CborRationalNumber TreasuryWithdrawal) : CborRecord;

// ──────────── Witness Mirrors ────────────

/// <summary>Mirror for <see cref="VKeyWitness"/>.</summary>
[CborSerializable]
[CborList]
public partial record VKeyWitnessMirror(
    [CborOrder(0)] ReadOnlyMemory<byte> VKey,
    [CborOrder(1)] ReadOnlyMemory<byte> Signature) : CborRecord;

// ──────────── Script Mirrors ────────────

/// <summary>Mirror for <see cref="PlutusV1Script"/>.</summary>
[CborSerializable]
[CborList]
public partial record PlutusV1ScriptMirror(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ReadOnlyMemory<byte> ScriptBytes) : CborRecord;

/// <summary>Mirror for <see cref="PlutusV2Script"/>.</summary>
[CborSerializable]
[CborList]
public partial record PlutusV2ScriptMirror(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ReadOnlyMemory<byte> ScriptBytes) : CborRecord;

/// <summary>Mirror for <see cref="PlutusV3Script"/>.</summary>
[CborSerializable]
[CborList]
public partial record PlutusV3ScriptMirror(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ReadOnlyMemory<byte> ScriptBytes) : CborRecord;

/// <summary>Mirror for <see cref="MultiSigScript"/>.</summary>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record MultiSigScriptMirror(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] INativeScript NativeScript) : CborRecord;

// ──────────── Datum Option Mirrors ────────────

/// <summary>Mirror for <see cref="InlineDatumOption"/>.</summary>
[CborSerializable]
[CborList]
public partial record InlineDatumOptionMirror(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] CborEncodedValue Data) : CborRecord;

/// <summary>Mirror for <see cref="DatumHashOption"/>.</summary>
[CborSerializable]
[CborList]
public partial record DatumHashOptionMirror(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ReadOnlyMemory<byte> DatumHash) : CborRecord;

// ──────────── Redeemer Mirrors ────────────

/// <summary>Mirror for <see cref="RedeemerEntry"/>.</summary>
[CborSerializable]
[CborList]
public partial record RedeemerEntryMirror(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ulong Index,
    [CborOrder(2)] IPlutusData Data,
    [CborOrder(3)] ExUnits ExUnits) : CborRecord;

/// <summary>Mirror for <see cref="RedeemerKey"/>.</summary>
[CborSerializable]
[CborList]
public partial record RedeemerKeyMirror(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ulong Index) : CborRecord;

/// <summary>Mirror for <see cref="RedeemerValue"/>.</summary>
[CborSerializable]
[CborList]
public partial record RedeemerValueMirror(
    [CborOrder(0)] IPlutusData Data,
    [CborOrder(1)] ExUnits ExUnits) : CborRecord;

/// <summary>Mirror for <see cref="RedeemerList"/>.</summary>
[CborSerializable]
public partial record RedeemerListMirror(List<RedeemerEntry> Value) : CborRecord;

/// <summary>Mirror for <see cref="RedeemerMap"/>.</summary>
[CborSerializable]
public partial record RedeemerMapMirror(Dictionary<RedeemerKey, RedeemerValue> Value) : CborRecord;

// ──────────── Collection Mirrors ────────────

/// <summary>Mirror for <see cref="TokenBundleOutput"/>.</summary>
[CborSerializable]
public partial record TokenBundleOutputMirror(Dictionary<ReadOnlyMemory<byte>, ulong> Value) : CborRecord;

/// <summary>Mirror for <see cref="TokenBundleMint"/>.</summary>
[CborSerializable]
public partial record TokenBundleMintMirror(Dictionary<ReadOnlyMemory<byte>, long> Value) : CborRecord;

/// <summary>Mirror for <see cref="MultiAssetOutput"/>.</summary>
[CborSerializable]
public partial record MultiAssetOutputMirror(Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> Value) : CborRecord;

/// <summary>Mirror for <see cref="MultiAssetMint"/>.</summary>
[CborSerializable]
public partial record MultiAssetMintMirror(Dictionary<ReadOnlyMemory<byte>, TokenBundleMint> Value) : CborRecord;

/// <summary>Mirror for <see cref="CborDefList{T}"/>.</summary>
[CborSerializable]
public partial record CborDefListMirror<T>(List<T> Value) : CborRecord;

/// <summary>Mirror for <see cref="CborDefListWithTag{T}"/>.</summary>
[CborSerializable]
[CborTag(258)]
public partial record CborDefListWithTagMirror<T>(List<T> Value) : CborRecord;

/// <summary>Mirror for <see cref="CborIndefList{T}"/>.</summary>
[CborSerializable]
public partial record CborIndefListMirror<T>(List<T> Value) : CborRecord;

/// <summary>Mirror for <see cref="CborDefList{T}"/> with long values.</summary>
[CborSerializable]
public partial record CborDefListLongMirror(List<long> Value) : CborRecord;

/// <summary>Mirror for <see cref="CborDefListWithTag{T}"/> with byte arrays.</summary>
[CborSerializable]
[CborTag(258)]
public partial record CborDefListWithTagBytesMirror(List<ReadOnlyMemory<byte>> Value) : CborRecord;

// ──────────── Metadata Mirrors ────────────

/// <summary>Mirror for <see cref="MetadataText"/>.</summary>
[CborSerializable]
public partial record MetadataTextMirror(string Value) : CborRecord;

/// <summary>Mirror for <see cref="MetadatumIntLong"/>.</summary>
[CborSerializable]
public partial record MetadatumIntLongMirror(long Value) : CborRecord;

/// <summary>Mirror for <see cref="MetadatumIntUlong"/>.</summary>
[CborSerializable]
public partial record MetadatumIntUlongMirror(ulong Value) : CborRecord;

/// <summary>Mirror for <see cref="MetadatumMap"/>.</summary>
[CborSerializable]
public partial record MetadatumMapMirror(Dictionary<ITransactionMetadatum, ITransactionMetadatum> Value) : CborRecord;

/// <summary>Mirror for <see cref="MetadatumList"/>.</summary>
[CborSerializable]
public partial record MetadatumListMirror(List<ITransactionMetadatum> Value) : CborRecord;

/// <summary>Mirror for <see cref="MetadatumBytes"/>.</summary>
[CborSerializable]
public partial record MetadatumBytesMirror(byte[] Value) : CborRecord;

// ──────────── Transaction Structure Mirrors ────────────

/// <summary>Mirror for <see cref="Credential"/>.</summary>
[CborSerializable]
[CborList]
public partial record CredentialMirror(
    [CborOrder(0)] int Type,
    [CborOrder(1)] ReadOnlyMemory<byte> Hash) : CborRecord;

/// <summary>Mirror for <see cref="PostMaryTransaction"/>.</summary>
[CborSerializable]
[CborList]
public partial record PostMaryTransactionMirror(
    [CborOrder(0)] ITransactionBody Body,
    [CborOrder(1)] ITransactionWitnessSet Witnesses,
    [CborOrder(2)] bool IsValid,
    [CborOrder(3)] IAuxiliaryData? AuxiliaryData) : CborRecord;

// ──────────── ConwayTransactionBody Mirror (CborMap) ────────────

/// <summary>Mirror for <see cref="ConwayTransactionBody"/>.</summary>
[CborSerializable]
[CborMap]
public partial record ConwayBodyMirror(
    [CborProperty(0)] ICborMaybeIndefList<TransactionInput> Inputs,
    [CborProperty(1)] ICborMaybeIndefList<ITransactionOutput> Outputs,
    [CborProperty(2)] ulong Fee,
    [CborProperty(3)] ulong? TimeToLive,
    [CborProperty(4)] ICborMaybeIndefList<ICertificate>? Certificates,
    [CborProperty(5)] Withdrawals? Withdrawals,
    [CborProperty(7)] ReadOnlyMemory<byte>? AuxiliaryDataHash,
    [CborProperty(8)] ulong? ValidityIntervalStart,
    [CborProperty(9)] MultiAssetMint? Mint,
    [CborProperty(11)] ReadOnlyMemory<byte>? ScriptDataHash,
    [CborProperty(13)] ICborMaybeIndefList<TransactionInput>? Collateral,
    [CborProperty(14)] ICborMaybeIndefList<ReadOnlyMemory<byte>>? RequiredSigners,
    [CborProperty(15)] int? NetworkId,
    [CborProperty(16)] ITransactionOutput? CollateralReturn,
    [CborProperty(17)] ulong? TotalCollateral,
    [CborProperty(18)] ICborMaybeIndefList<TransactionInput>? ReferenceInputs,
    [CborProperty(19)] VotingProcedures? VotingProcedures,
    [CborProperty(20)] ICborMaybeIndefList<ProposalProcedure>? ProposalProcedures,
    [CborProperty(21)] ulong? TreasuryValue,
    [CborProperty(22)] ulong? Donation) : CborRecord;

// ──────────── Witness Set Mirror (CborMap) ────────────

/// <summary>Mirror for <see cref="PostAlonzoTransactionWitnessSet"/>.</summary>
[CborSerializable]
[CborMap]
public partial record WitnessSetMirror(
    [CborProperty(0)] ICborMaybeIndefList<VKeyWitness>? VKeyWitnesses,
    [CborProperty(1)] ICborMaybeIndefList<INativeScript>? NativeScripts,
    [CborProperty(2)] ICborMaybeIndefList<BootstrapWitness>? BootstrapWitnesses,
    [CborProperty(3)] ICborMaybeIndefList<ReadOnlyMemory<byte>>? PlutusV1Scripts,
    [CborProperty(4)] ICborMaybeIndefList<IPlutusData>? PlutusDataSet,
    [CborProperty(5)] IRedeemers? Redeemers,
    [CborProperty(6)] ICborMaybeIndefList<ReadOnlyMemory<byte>>? PlutusV2Scripts,
    [CborProperty(7)] ICborMaybeIndefList<ReadOnlyMemory<byte>>? PlutusV3Scripts) : CborRecord;

// ──────────── Auxiliary Data Mirror (CborMap) ────────────

/// <summary>Mirror for <see cref="PostAlonzoAuxiliaryDataMap"/>.</summary>
[CborSerializable]
[CborMap]
[CborTag(259)]
public partial record AuxiliaryDataMirror(
    [CborProperty(0)] Metadata? TransactionMetadata,
    [CborProperty(1)] ICborMaybeIndefList<INativeScript>? NativeScripts,
    [CborProperty(2)] ICborMaybeIndefList<ReadOnlyMemory<byte>>? PlutusV1Scripts,
    [CborProperty(3)] ICborMaybeIndefList<ReadOnlyMemory<byte>>? PlutusV2Scripts,
    [CborProperty(4)] ICborMaybeIndefList<ReadOnlyMemory<byte>>? PlutusV3Scripts) : CborRecord;

// ──────────── Metadata Mirror ────────────

/// <summary>Mirror for <see cref="Metadata"/>.</summary>
[CborSerializable]
public partial record MetadataMirror(Dictionary<ulong, ITransactionMetadatum> Value) : CborRecord;
