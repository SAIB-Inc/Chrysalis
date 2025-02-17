using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Exceptions;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class BytesConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        if (reader.PeekState() != CborReaderState.StartIndefiniteLengthByteString)
            return reader.ReadByteString();

        // Handle indefinite length
        using var stream = new MemoryStream();
        reader.ReadStartIndefiniteLengthByteString();

        while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString)
        {
            byte[] chunk = reader.ReadByteString();
            stream.Write(chunk);
        }

        reader.ReadEndIndefiniteLengthByteString();
        return stream.ToArray();
    }

    public void Write(CborWriter writer, object? value, CborOptions options)
    {
        if (value is not byte[] v)
            throw new CborTypeMismatchException("Value is not a byte array", typeof(byte[]));

        if (options.IsDefinite)
        {
            writer.WriteByteString(v);
            return;
        }

        writer.WriteStartIndefiniteLengthByteString();
        writer.WriteByteString(v);
        writer.WriteEndIndefiniteLengthByteString();
    }
}