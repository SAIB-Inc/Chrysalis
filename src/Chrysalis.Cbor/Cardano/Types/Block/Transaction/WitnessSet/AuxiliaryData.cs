using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

// [CborSerializable]
[CborUnion]
public abstract partial record AuxiliaryData : CborBase<AuxiliaryData>
{
    // [CborSerializable]
    [CborMap]
    [CborTag(259)]
    public partial record PostAlonzoAuxiliaryDataMap(
        [CborIndex(0)] Metadata? MetadataValue,
        [CborIndex(1)] CborMaybeIndefList<NativeScript>.CborDefList? NativeScriptSet,
        [CborIndex(2)] CborMaybeIndefList<byte[]>.CborDefList? PlutusV1ScriptSet,
        [CborIndex(3)] CborMaybeIndefList<byte[]>.CborDefList? PlutusV2ScriptSet,
        [CborIndex(4)] CborMaybeIndefList<byte[]>.CborDefList? PlutusV3ScriptSet
    ) : AuxiliaryData;


    // [CborSerializable]
    public partial record Metadata(Dictionary<ulong, TransactionMetadatum> Value) : AuxiliaryData;


    // [CborSerializable]
    [CborList]
    public partial record ShellyMaAuxiliaryData(
       [CborIndex(0)] Metadata TransactionMetadata,
       [CborIndex(1)] CborMaybeIndefList<NativeScript>.CborDefList AuxiliaryScripts
   ) : AuxiliaryData;
}
