using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization.Exceptions;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class EncodedValueConverter : ICborConverter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? Read(CborReader reader, CborOptions options)
    {
        return reader.ReadByteString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(CborWriter writer, object? value, CborOptions options)
    {
        if (value is not byte[] v)
            throw new CborTypeMismatchException("Value is not a bytes", typeof(byte[]));

        writer.WriteByteString(v);
    }
}