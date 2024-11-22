using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Attributes;
using Chrysalis.Types.Core;

namespace Chrysalis.Converters
{
    public class CborConstrConverter : ICborConverter<CborConstr>
    {
        public ReadOnlyMemory<byte> Serialize(CborConstr value)
        {
            // Get the CborSerializable attribute from the type of the current instance
            CborSerializableAttribute attribute = value.GetType().GetCustomAttribute<CborSerializableAttribute>()
                ?? throw new InvalidOperationException($"The type {value.GetType().Name} is not marked with CborSerializableAttribute.");

            // Get the properties of the current instance
            PropertyInfo[] properties = value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            CborWriter writer = new();

            // Write the tag
            writer.WriteTag(GetTag(attribute.Index));

            // Determine if the array is definite or indefinite
            // then write the array
            writer.WriteStartArray(attribute.IsDefinite ? (properties?.Length - 1) ?? 0 : null);

            // Iterate over each property
            properties?.ToList().ForEach(property =>
            {
                object? propertyValue = property.GetValue(value);

                // Check if the property implements ICbor
                if (propertyValue is not ICbor cborValue)
                    return;

                // Find the CborSerializableAttribute for the property's type
                var propertyAttribute = property.PropertyType.GetCustomAttribute<CborSerializableAttribute>();
                if (propertyAttribute == null || propertyAttribute.Converter == null)
                {
                    // If no converter is found, just skip or throw an exception if desired
                    throw new InvalidOperationException($"Property {property.Name} of type {property.PropertyType.Name} does not have a valid CborSerializable attribute with a Converter.");
                }

                // Get the converter type and create an instance
                var converterType = propertyAttribute.Converter;
                var genericInterface = converterType.GetInterfaces()
                    .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICborConverter<>))
                    ?? throw new InvalidOperationException($"Converter type {converterType.Name} does not implement ICborConverter<T>.");

                // Create an instance of the converter
                var converter = Activator.CreateInstance(converterType)
                    ?? throw new InvalidOperationException($"Could not create an instance of the converter {converterType.Name}.");

                // Invoke the Serialize method on the converter dynamically
                var serializeMethod = genericInterface.GetMethod("Serialize")
                    ?? throw new InvalidOperationException($"Serialize method not found on converter {converterType.Name}.");
                var serializedValue = serializeMethod.Invoke(converter, new[] { propertyValue });

                // Ensure that the result is of the expected type
                if (serializedValue is not ReadOnlyMemory<byte> serializedBytes)
                    throw new InvalidOperationException($"Serialize method did not return a ReadOnlyMemory<byte> for property {property.Name}.");


                // Write the encoded value
                writer.WriteEncodedValue(serializedBytes.ToArray());
            });

            writer.WriteEndArray();

            // Return the encoded bytes
            return writer.Encode();
        }

        public CborConstr Deserialize(ReadOnlyMemory<byte> data)
        {
            throw new NotImplementedException();
        }

        private static CborTag GetTag(int index)
        {
            int finalIndex = index > 6 ? 1280 - 7 : 121;
            return (CborTag)(finalIndex + index);
        }
    }
}
