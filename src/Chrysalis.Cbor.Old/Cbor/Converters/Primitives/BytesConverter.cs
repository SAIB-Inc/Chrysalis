using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class BytesConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        // SampleByte(byte[] value) 
        if (reader.PeekState() != CborReaderState.StartIndefiniteLengthByteString)
        {
            return reader.ReadByteString();
        }
        reader.ReadStartIndefiniteLengthByteString();
        List<byte[]> chunks = [];
        // @TODO: Use a readonlymemory/span for this
        while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString)
        {
            chunks.Add(reader.ReadByteString());
        }
        reader.ReadEndIndefiniteLengthByteString();
        return chunks.SelectMany(x => x);
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        byte[] bytes = (byte[])value;
        if (options?.IsDefinite == true)
        {
            writer.WriteByteString(bytes);
            return;
        }
        writer.WriteStartIndefiniteLengthByteString();
        writer.WriteByteString(bytes);
        writer.WriteEndIndefiniteLengthByteString();
    }
}