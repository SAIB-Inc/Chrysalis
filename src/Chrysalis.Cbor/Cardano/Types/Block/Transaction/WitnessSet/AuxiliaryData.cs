using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

[CborSerializable]
[CborUnion]
public abstract partial record AuxiliaryData : CborBase<AuxiliaryData>
{
    [CborSerializable]
    [CborMap]
    [CborTag(259)]
    public partial record PostAlonzoAuxiliaryDataMap(
        [CborProperty("0")] Metadata? MetadataValue,
        [CborProperty("1")] CborMaybeIndefList<NativeScript>.CborDefList? NativeScriptSet,
        [CborProperty("2")] CborMaybeIndefList<byte[]>.CborDefList? PlutusV1ScriptSet,
        [CborProperty("3")] CborMaybeIndefList<byte[]>.CborDefList? PlutusV2ScriptSet,
        [CborProperty("4")] CborMaybeIndefList<byte[]>.CborDefList? PlutusV3ScriptSet
    ) : AuxiliaryData;


    [CborSerializable]
    public partial record Metadata(Dictionary<ulong, TransactionMetadatum> Value) : AuxiliaryData;


    [CborSerializable]
    [CborList]
    public partial record ShellyMaAuxiliaryData(
       [CborOrder(0)] Metadata TransactionMetadata,
       [CborOrder(1)] CborMaybeIndefList<NativeScript>.CborDefList AuxiliaryScripts
   ) : AuxiliaryData;
}
