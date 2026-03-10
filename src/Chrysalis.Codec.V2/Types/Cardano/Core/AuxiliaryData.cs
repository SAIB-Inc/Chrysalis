using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;
using Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.V2.Types.Cardano.Core.Scripts;

namespace Chrysalis.Codec.V2.Types.Cardano.Core;

[CborSerializable]
[CborUnion]
public partial interface IAuxiliaryData : ICborType;

[CborSerializable]
[CborMap]
[CborTag(259)]
public readonly partial record struct PostAlonzoAuxiliaryDataMap : IAuxiliaryData
{
    [CborProperty(0)] public partial Metadata? TransactionMetadata { get; }
    [CborProperty(1)] public partial ICborMaybeIndefList<INativeScript>? NativeScripts { get; }
    [CborProperty(2)] public partial ICborMaybeIndefList<ReadOnlyMemory<byte>>? PlutusV1Scripts { get; }
    [CborProperty(3)] public partial ICborMaybeIndefList<ReadOnlyMemory<byte>>? PlutusV2Scripts { get; }
    [CborProperty(4)] public partial ICborMaybeIndefList<ReadOnlyMemory<byte>>? PlutusV3Scripts { get; }
}

[CborSerializable]
public readonly partial record struct Metadata : IAuxiliaryData
{
    public partial Dictionary<ulong, ITransactionMetadatum> Value { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ShellyMaAuxiliaryData : IAuxiliaryData
{
    [CborOrder(0)] public partial Metadata TransactionMetadata { get; }
    [CborOrder(1)] public partial ICborMaybeIndefList<INativeScript> AuxiliaryScripts { get; }
}

[CborSerializable]
public partial record AuxiliaryDataSet(Dictionary<int, IAuxiliaryData> Value) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}
