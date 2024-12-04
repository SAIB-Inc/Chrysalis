using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Attributes;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class BytesConverter : ICborConverter<CborBytes>
{
    public byte[] Serialize(CborBytes data)
    {
        Type type = data.GetType();
        bool isDefinite = type.GetCustomAttribute<CborDefiniteAttribute>() != null;
        CborSizeAttribute? sizeAttr = type.GetCustomAttribute<CborSizeAttribute>();

        // Write the bytes either as a definite or indefinite length byte string
        CborWriter writer = new();
        if (isDefinite && sizeAttr is not null)
        {
            writer.WriteStartIndefiniteLengthByteString();
            data.Value.Chunk(sizeAttr.Size).ToList().ForEach(writer.WriteByteString);
            writer.WriteEndIndefiniteLengthByteString();
        }
        else
        {
            writer.WriteByteString(data.Value);
        }

        return writer.Encode();
    }

    public ICbor Deserialize(byte[] data, Type? targetType = null)
    {
        CborReader reader = new(data);

        if (reader.PeekState() == CborReaderState.StartIndefiniteLengthByteString)
        {
            reader.ReadStartIndefiniteLengthByteString();

            List<byte[]> chunks = [];
            while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString) chunks.Add(reader.ReadByteString());
            reader.ReadEndIndefiniteLengthByteString();

            return new CborBytes(chunks.SelectMany(x => x).ToArray());
        }

        return new CborBytes(reader.ReadByteString());
    }
}