using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Types.Cardano.Core;

[CborSerializable]
[CborUnion]
public abstract partial record AuxiliaryData : CborBase { }

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

[CborSerializable]
public partial record Metadata(Dictionary<ulong, TransactionMetadatum> Value) : AuxiliaryData;

[CborSerializable]
[CborList]
public partial record ShellyMaAuxiliaryData(
   [CborOrder(0)] Metadata TransactionMetadata,
   [CborOrder(1)] CborDefList<NativeScript> AuxiliaryScripts
) : AuxiliaryData, ICborPreserveRaw;

[CborSerializable]
public partial record AuxiliaryDataSet(Dictionary<int, AuxiliaryData> Value) : CborBase;