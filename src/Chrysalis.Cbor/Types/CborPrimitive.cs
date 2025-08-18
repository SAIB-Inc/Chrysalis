using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// Union type for CBOR primitive values
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record CborPrimitive : CborBase { }

[CborSerializable]
public partial record CborBool(bool Value) : CborPrimitive;

[CborSerializable]
public partial record CborInt(int Value) : CborPrimitive;

[CborSerializable]
public partial record CborLong(long Value) : CborPrimitive;

[CborSerializable]
public partial record CborString(string Value) : CborPrimitive;

[CborSerializable]
public partial record CborBytes(byte[] Value) : CborPrimitive;