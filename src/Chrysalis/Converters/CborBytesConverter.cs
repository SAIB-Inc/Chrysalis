using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Attributes;
using Chrysalis.Types.Core;

namespace Chrysalis.Converters;

public class CborBytesConverter : ICborConverter<CborBytes>
{
    public ReadOnlyMemory<byte> Serialize(CborBytes data)
    {
        // Get the CborSerializable attribute from the type of the current instance
        CborSerializableAttribute attribute = data.GetType().GetCustomAttribute<CborSerializableAttribute>()
            ?? throw new InvalidOperationException($"The type {data.GetType().Name} is not marked with CborSerializableAttribute.");

        // Write the bytes either as a definite or indefinite length byte string
        CborWriter writer = new();
        if (attribute.IsDefinite)
        {
            writer.WriteStartIndefiniteLengthByteString();
            data.Value.Chunk(attribute.Size).ToList().ForEach(writer.WriteByteString);
            writer.WriteEndIndefiniteLengthByteString();
        }
        else
        {
            writer.WriteByteString(data.Value);
        }

        return writer.Encode();
    }

    public CborBytes Deserialize(ReadOnlyMemory<byte> data)
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