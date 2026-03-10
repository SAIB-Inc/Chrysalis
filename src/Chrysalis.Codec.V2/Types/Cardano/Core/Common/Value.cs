using System.Buffers;
using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public partial interface IValue : ICborType;

[CborSerializable]
public readonly partial record struct Lovelace : IValue
{
    [CborOrder(0)] public partial ulong Amount { get; }

    public static Lovelace FromAmount(ulong amount)
    {
        ArrayBufferWriter<byte> buffer = new(9);
        CborWriter writer = new(buffer);
        writer.WriteUInt64(amount);
        return Read(buffer.WrittenMemory);
    }
}

[CborSerializable]
[CborList]
public readonly partial record struct LovelaceWithMultiAsset : IValue
{
    [CborOrder(0)] public partial ulong Amount { get; }
    [CborOrder(1)] public partial MultiAssetOutput MultiAsset { get; }
}
