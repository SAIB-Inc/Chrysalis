using System.Collections.Generic;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// CBOR primitive value union type that can hold any CBOR primitive value type-safely.
/// Used for scenarios where we need to store different primitive types (like COSE header values).
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record CborPrimitive : CborBase 
{
    // Implicit conversions for convenience
    public static implicit operator CborPrimitive(int value) => new CborInt(value);
    public static implicit operator CborPrimitive(long value) => new CborLong(value);
    public static implicit operator CborPrimitive(uint value) => new CborUInt(value);
    public static implicit operator CborPrimitive(ulong value) => new CborULong(value);
    public static implicit operator CborPrimitive(string value) => new CborString(value);
    public static implicit operator CborPrimitive(bool value) => new CborBool(value);
    public static implicit operator CborPrimitive(byte[] value) => new CborBytes(value);
    public static implicit operator CborPrimitive(float value) => new CborFloat(value);
    public static implicit operator CborPrimitive(double value) => new CborDouble(value);
    public static implicit operator CborPrimitive(List<CborPrimitive> value) => new CborArray(value);
    public static implicit operator CborPrimitive(Dictionary<CborLabel, CborPrimitive> value) => new CborMap(value);
}

[CborSerializable]
public partial record CborInt(int Value) : CborPrimitive;

[CborSerializable]
public partial record CborLong(long Value) : CborPrimitive;

[CborSerializable]
public partial record CborUInt(uint Value) : CborPrimitive;

[CborSerializable]
public partial record CborULong(ulong Value) : CborPrimitive;

[CborSerializable]
public partial record CborString(string Value) : CborPrimitive;

[CborSerializable]
public partial record CborBool(bool Value) : CborPrimitive;

[CborSerializable]
public partial record CborBytes(byte[] Value) : CborPrimitive;

[CborSerializable]
public partial record CborFloat(float Value) : CborPrimitive;

[CborSerializable]
public partial record CborDouble(double Value) : CborPrimitive;

[CborSerializable]
public partial record CborNull(bool IsNull) : CborPrimitive
{
    public CborNull() : this(true) { }
    public static CborPrimitive Null { get; } = new CborNull();
}

[CborSerializable]
public partial record CborArray(List<CborPrimitive> Value) : CborPrimitive
{
    public CborArray() : this([]) { }
}

[CborSerializable]
public partial record CborMap(Dictionary<CborLabel, CborPrimitive> Value) : CborPrimitive
{
    public CborMap() : this(new Dictionary<CborLabel, CborPrimitive>()) { }
}