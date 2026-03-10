using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Byron;

[CborSerializable]
[CborList]
public readonly partial record struct ByronTxPayload : ICborType
{
    [CborOrder(0)] public partial ByronTx Transaction { get; }
    [CborOrder(1)] public partial ICborMaybeIndefList<ByronTxWitness> Witnesses { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronTx : ICborType
{
    [CborOrder(0)] public partial ICborMaybeIndefList<ByronTxIn> Inputs { get; }
    [CborOrder(1)] public partial ICborMaybeIndefList<ByronTxOut> Outputs { get; }
    [CborOrder(2)] public partial CborEncodedValue Attributes { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronTxOut : ICborType
{
    [CborOrder(0)] public partial ByronAddress Address { get; }
    [CborOrder(1)] public partial ulong Amount { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronTxIn : ICborType
{
    [CborOrder(0)] public partial int Variant { get; }
    [CborOrder(1)] public partial CborEncodedValue Data { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronAddress : ICborType
{
    [CborOrder(0)] public partial CborEncodedValue Payload { get; }
    [CborOrder(1)] public partial ulong Crc { get; }
}
