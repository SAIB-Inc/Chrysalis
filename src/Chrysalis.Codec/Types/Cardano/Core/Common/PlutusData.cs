using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public partial interface IPlutusData : ICborType;

[CborSerializable]
[CborConstr]
public readonly partial record struct PlutusConstr : IPlutusData
{
    [CborOrder(0)] public partial ICborMaybeIndefList<IPlutusData> Fields { get; }
}

[CborSerializable]
public readonly partial record struct PlutusMap : IPlutusData
{
    public partial Dictionary<IPlutusData, IPlutusData> Value { get; }
}

[CborSerializable]
public readonly partial record struct PlutusList : IPlutusData
{
    public partial ICborMaybeIndefList<IPlutusData> Value { get; }
}

[CborSerializable]
[CborUnion]
public partial interface IPlutusBigInt : IPlutusData;

[CborSerializable]
public readonly partial record struct PlutusUint64 : IPlutusBigInt
{
    public partial ulong Value { get; }
}

[CborSerializable]
public readonly partial record struct PlutusInt64 : IPlutusBigInt
{
    public partial long Value { get; }
}

[CborSerializable]
public readonly partial record struct PlutusInt : IPlutusBigInt
{
    public partial int Value { get; }
}

[CborSerializable]
[CborTag(2)]
public readonly partial record struct PlutusBigUint : IPlutusBigInt
{
    public partial ReadOnlyMemory<byte> Value { get; }
}

[CborSerializable]
[CborTag(3)]
public readonly partial record struct PlutusBigNint : IPlutusBigInt
{
    public partial ReadOnlyMemory<byte> Value { get; }
}

[CborSerializable]
public readonly partial record struct PlutusBoundedBytes : IPlutusData
{
    public partial ReadOnlyMemory<byte> Value { get; }
}

[CborSerializable]
[CborUnion]
public partial interface IPlutusBool : IPlutusData;

[CborSerializable]
[CborConstr(0)]
public readonly partial record struct PlutusFalse : IPlutusBool;

[CborSerializable]
[CborConstr(1)]
public readonly partial record struct PlutusTrue : IPlutusBool;
