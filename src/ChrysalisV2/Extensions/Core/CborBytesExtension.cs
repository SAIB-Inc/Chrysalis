using System.Formats.Cbor;
using System.Reflection;
using ChrysalisV2.Attributes;
using ChrysalisV2.Types.Core;

namespace ChrysalisV2.Extensions.Core;

public static class CborBytesExtension
{
    public static byte[] Serialize(this CborBytes self)
    {
        // Get the CborSerializable attribute from the type of the current instance
        CborSerializableAttribute attribute = self.GetType().GetCustomAttribute<CborSerializableAttribute>()
            ?? throw new InvalidOperationException($"The type {self.GetType().Name} is not marked with CborSerializableAttribute.");

        // Write the bytes either as a definite or indefinite length byte string
        CborWriter writer = new();
        if (attribute.IsDefinite)
        {
            writer.WriteStartIndefiniteLengthByteString();
            self.Value.Chunk(attribute.Size).ToList().ForEach(writer.WriteByteString);
            writer.WriteEndIndefiniteLengthByteString();
        }
        else
        {
            writer.WriteByteString(self.Value);
        }

        return writer.Encode();
    }

    public static CborBytes Deserialize(this byte[] self)
    {
        throw new NotImplementedException();
    }
}