using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;

namespace Chrysalis.Codec.Types.Cardano.Core;

[CborSerializable]
[CborUnion]
public partial interface IAuxiliaryData : ICborType;

[CborSerializable]
[CborMap]
[CborTag(259)]
public readonly partial record struct PostAlonzoAuxiliaryDataMap : IAuxiliaryData
{
    [CborProperty(0)] public partial Metadata? TransactionMetadata { get; }
    [CborProperty(1)] public partial CborList<INativeScript>? NativeScripts { get; }
    [CborProperty(2)] public partial CborList<ReadOnlyMemory<byte>>? PlutusV1Scripts { get; }
    [CborProperty(3)] public partial CborList<ReadOnlyMemory<byte>>? PlutusV2Scripts { get; }
    [CborProperty(4)] public partial CborList<ReadOnlyMemory<byte>>? PlutusV3Scripts { get; }
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
    [CborOrder(1)] public partial CborList<INativeScript> AuxiliaryScripts { get; }
}

[CborSerializable]
public readonly partial record struct AuxiliaryDataSet : ICborType
{
    public partial CborMap<int, IAuxiliaryData> Value { get; }
}
