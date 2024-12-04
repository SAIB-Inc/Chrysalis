using Chrysalis.Attributes;
using Chrysalis.Converters;

namespace Chrysalis.Types;

public interface ICbor
{
    byte[]? Raw { get; set; }
}

public record RawCbor : ICbor
{
    public byte[]? Raw { get; set; }
}

[CborConverter(typeof(BoolConverter))]
public record CborBool(bool Value) : RawCbor;

[CborConverter(typeof(IntConverter))]
public record CborInt(int Value) : RawCbor;

[CborConverter(typeof(LongConverter))]
public record CborLong(long Value) : RawCbor;

[CborConverter(typeof(UlongConverter))]
public record CborUlong(ulong Value) : RawCbor;

[CborConverter(typeof(BytesConverter))]
public record CborBytes(byte[] Value) : RawCbor;

[CborConverter(typeof(ListConverter<>))]
public record CborList<T>(List<T> Value) : RawCbor where T : ICbor;

[CborConverter(typeof(MapConverter<,>))]
public record CborMap<TKey, TValue>(Dictionary<TKey, TValue> Value) : RawCbor
    where TKey : ICbor
    where TValue : ICbor;

[CborConverter(typeof(MaybeConverter<>))]
public record CborMaybe<T>(T? Value) : RawCbor where T : ICbor;

[CborConverter(typeof(TextConverter))]
public record CborText(string Value) : RawCbor;

[CborConverter(typeof(RationalNumberConverter))]
public record CborRationalNumber(ulong Numerator, ulong Denominator) : RawCbor;

[CborConverter(typeof(EncodedValueConverter))]
public record CborEncodedValue(byte[] Value) : RawCbor;

[CborConverter(typeof(ConstrConverter))]
public abstract record CborConstr : RawCbor;