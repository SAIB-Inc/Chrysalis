using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Types.Cardano.Core;

/// <summary>
/// Represents auxiliary data attached to a Cardano transaction.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record AuxiliaryData : CborBase { }

/// <summary>
/// Represents post-Alonzo auxiliary data containing metadata, native scripts, and Plutus scripts.
/// </summary>
/// <param name="MetadataValue">The optional transaction metadata.</param>
/// <param name="NativeScriptSet">The optional set of native scripts.</param>
/// <param name="PlutusV1ScriptSet">The optional set of Plutus V1 scripts.</param>
/// <param name="PlutusV2ScriptSet">The optional set of Plutus V2 scripts.</param>
/// <param name="PlutusV3ScriptSet">The optional set of Plutus V3 scripts.</param>
[CborSerializable]
[CborMap]
[CborTag(259)]
public partial record PostAlonzoAuxiliaryDataMap(
    [CborProperty(0)] Metadata? MetadataValue,
    [CborProperty(1)] CborDefList<NativeScript>? NativeScriptSet,
    [CborProperty(2)] CborDefList<byte[]>? PlutusV1ScriptSet,
    [CborProperty(3)] CborDefList<byte[]>? PlutusV2ScriptSet,
    [CborProperty(4)] CborDefList<byte[]>? PlutusV3ScriptSet
) : AuxiliaryData, ICborPreserveRaw;

/// <summary>
/// Represents transaction metadata as a dictionary of metadatum entries keyed by label.
/// </summary>
/// <param name="Value">The dictionary mapping metadata labels to their values.</param>
[CborSerializable]
public partial record Metadata(Dictionary<ulong, TransactionMetadatum> Value) : AuxiliaryData;

/// <summary>
/// Represents Shelley/Mary era auxiliary data containing metadata and native scripts.
/// </summary>
/// <param name="TransactionMetadata">The transaction metadata.</param>
/// <param name="AuxiliaryScripts">The auxiliary native scripts.</param>
[CborSerializable]
[CborList]
public partial record ShellyMaAuxiliaryData(
   [CborOrder(0)] Metadata TransactionMetadata,
   [CborOrder(1)] CborDefList<NativeScript> AuxiliaryScripts
) : AuxiliaryData, ICborPreserveRaw;

/// <summary>
/// Represents a set of auxiliary data entries keyed by transaction index within a block.
/// </summary>
/// <param name="Value">The dictionary mapping transaction indices to their auxiliary data.</param>
[CborSerializable]
public partial record AuxiliaryDataSet(Dictionary<int, AuxiliaryData> Value) : CborBase;
