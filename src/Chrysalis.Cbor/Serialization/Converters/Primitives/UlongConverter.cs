using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization.Exceptions;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class UlongConverter : ICborConverter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? Read(CborReader reader, CborOptions options)
    {
        return reader.ReadUInt64();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        if (value.First() is not ulong v)
            throw new CborTypeMismatchException("Value is not a ulong", typeof(ulong));

        writer.WriteUInt64(v);
    }
}