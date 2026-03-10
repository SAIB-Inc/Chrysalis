using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types;

[CborSerializable]
[CborUnion]
public partial interface ICborPrimitive : ICborType;

[CborSerializable]
public readonly partial record struct CborInt : ICborPrimitive
{
    public partial int Value { get; }
}

[CborSerializable]
public readonly partial record struct CborLong : ICborPrimitive
{
    public partial long Value { get; }
}

[CborSerializable]
public readonly partial record struct CborUInt : ICborPrimitive
{
    public partial uint Value { get; }
}

[CborSerializable]
public readonly partial record struct CborULong : ICborPrimitive
{
    public partial ulong Value { get; }
}

[CborSerializable]
public readonly partial record struct CborString : ICborPrimitive
{
    public partial string Value { get; }
}

[CborSerializable]
public readonly partial record struct CborBool : ICborPrimitive
{
    public partial bool Value { get; }
}

[CborSerializable]
public readonly partial record struct CborBytes : ICborPrimitive
{
    public partial byte[] Value { get; }
}

[CborSerializable]
public readonly partial record struct CborFloat : ICborPrimitive
{
    public partial float Value { get; }
}

[CborSerializable]
public readonly partial record struct CborDouble : ICborPrimitive
{
    public partial double Value { get; }
}

[CborSerializable]
public readonly partial record struct CborNull : ICborPrimitive
{
    public partial bool IsNull { get; }
}

[CborSerializable]
public readonly partial record struct CborArray : ICborPrimitive
{
    public partial List<ICborPrimitive> Value { get; }
}

[CborSerializable]
public readonly partial record struct CborMap : ICborPrimitive
{
    public partial Dictionary<ICborPrimitive, ICborPrimitive> Value { get; }
}
