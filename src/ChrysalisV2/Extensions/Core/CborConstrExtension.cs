using System.Formats.Cbor;
using System.Reflection;
using ChrysalisV2.Attributes;
using ChrysalisV2.Types.Core;

namespace ChrysalisV2.Extensions.Core;

public static class CborConstrExtension
{
    public static byte[] Serialize(this CborConstr self)
    {
        // Get the CborSerializable attribute from the type of the current instance
        CborSerializableAttribute attribute = self.GetType().GetCustomAttribute<CborSerializableAttribute>()
            ?? throw new InvalidOperationException($"The type {self.GetType().Name} is not marked with CborSerializableAttribute.");

        // Get the properties of the current instance
        PropertyInfo[] properties = self.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        CborWriter writer = new();

        // Write the tag
        writer.WriteTag(GetTag(attribute.Index));

        // Determine if the array is definite or indefinite
        // then write the array
        writer.WriteStartArray(attribute.IsDefinite ? properties?.Length ?? 0 : null);
        properties?.ToList().ForEach(property =>
        {
            object? value = property.GetValue(self);

            // Check if the property implements ICbor
            // otherwise skip the property
            if (value is not ICbor cborValue) return;

            writer.WriteEncodedValue(cborValue.Serialize());
        });
        writer.WriteEndArray();

        // Return the encoded bytes
        return writer.Encode();
    }

    public static CborConstr Deserialize(this byte[] self)
    {
        throw new NotImplementedException();
    }

    private static CborTag GetTag(int index)
    {
        int finalIndex = index > 6 ? 1280 - 7 : 121;
        return (CborTag)(finalIndex + index);
    }
}