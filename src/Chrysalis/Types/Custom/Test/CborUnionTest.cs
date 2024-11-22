using Chrysalis.Attributes;
using Chrysalis.Converters;
using Chrysalis.Types.Core;

namespace Chrysalis.Types.Custom.Test;

[CborSerializable(Converter = typeof(CborUnionConverter))]
public interface ICborUnionBasic : ICborUnion;

[CborSerializable(Converter = typeof(CborBoolConverter))]
public record UnionBool(bool Value) : CborBool(Value), ICborUnionBasic;

[CborSerializable(Converter = typeof(CborBytesConverter))]
public record UnionBytes(byte[] Value) : CborBytes(Value), ICborUnionBasic;

[CborSerializable(Converter = typeof(CborBytesConverter))]
public record UnionBoundedBytes(byte[] Value) : CborBytes(Value), ICborUnionBasic;