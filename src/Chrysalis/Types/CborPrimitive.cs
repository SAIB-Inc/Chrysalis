using Chrysalis.Attributes;
using Chrysalis.Converters;

namespace Chrysalis.Types;

public interface ICbor
{
    // No Raw property here
}

public abstract record Cbor : ICbor
{
    public byte[]? Raw { get; set; }
}

[CborConverter(typeof(BoolConverter))]
public record CborBool(bool Value) : Cbor;

[CborConverter(typeof(IntConverter))]
public record CborInt(int Value) : Cbor;

[CborConverter(typeof(LongConverter))]
public record CborLong(long Value) : Cbor;

[CborConverter(typeof(UlongConverter))]
public record CborUlong(ulong Value) : Cbor;

[CborConverter(typeof(BytesConverter))]
public record CborBytes(byte[] Value) : Cbor;

[CborConverter(typeof(ListConverter))]
public record CborList<T>(List<T> Value) : Cbor where T : ICbor;

[CborConverter(typeof(MapConverter))]
public record CborMap<TKey, TValue>(Dictionary<TKey, TValue> Value) : Cbor
    where TKey : Cbor
    where TValue : Cbor;

[CborConverter(typeof(MaybeConverter))]
public record CborMaybe<T>(T? Value) : Cbor where T : ICbor;

[CborConverter(typeof(TextConverter))]
public record CborText(string Value) : Cbor;

[CborConverter(typeof(RationalNumberConverter))]
public record CborRationalNumber(ulong Numerator, ulong Denominator) : Cbor;

[CborConverter(typeof(EncodedValueConverter))]
public record CborEncodedValue(byte[] Value) : Cbor;

[CborConverter(typeof(ConstrConverter))]
public abstract record CborConstr : Cbor;