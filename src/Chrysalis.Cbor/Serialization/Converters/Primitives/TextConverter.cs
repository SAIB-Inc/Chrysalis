using System.Formats.Cbor;
using System.Runtime.CompilerServices;
using Chrysalis.Cbor.Serialization.Exceptions;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class TextConverter : ICborConverter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? Read(CborReader reader, CborOptions options)
    {
        return reader.ReadTextString();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(CborWriter writer, object? value, CborOptions options)
    {
        if (value is not string v)
            throw new CborTypeMismatchException("Value is not a string", typeof(string));

        writer.WriteTextString(v);
    }
}