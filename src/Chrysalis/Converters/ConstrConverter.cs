using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Attributes;
using Chrysalis.Types;

namespace Chrysalis.Converters
{
    public class ConstrConverter : ICborConverter<CborConstr>
    {
        public byte[] Serialize(CborConstr value)
        {
            Type type = value.GetType();
            int index = type.GetCustomAttribute<CborIndexAttribute>()?.Index ?? 0;

            bool isDefinite = type.GetCustomAttribute<CborDefiniteAttribute>() is not null;

            PropertyInfo[] properties = [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<CborPropertyAttribute>() != null)
                .OrderBy(p => p.GetCustomAttribute<CborPropertyAttribute>()!.Index)];

            CborWriter writer = new();

            // Write the tag
            writer.WriteTag(GetTag(index));

            // Write array start
            writer.WriteStartArray(isDefinite ? properties.Length : null);

            // Serialize properties
            foreach (PropertyInfo? property in properties)
            {
                if (value == null) continue;

                CborConverterAttribute? converterAttr = property.PropertyType.GetCustomAttribute<CborConverterAttribute>() ?? throw new InvalidOperationException($"Property {property.Name} type must have a CborConverter attribute");
                object converter = Activator.CreateInstance(converterAttr.ConverterType)
                    ?? throw new InvalidOperationException($"Could not create converter instance for {property.Name}");

                // Get serialize method through reflection
                MethodInfo method = converterAttr.ConverterType.GetMethod("Serialize")
                    ?? throw new InvalidOperationException($"Converter must implement Serialize method");

                byte[] serialized = (byte[])method.Invoke(converter, new[] { value })!;
                writer.WriteEncodedValue(serialized);
            }

            writer.WriteEndArray();
            return [.. writer.Encode()];
        }

        public ICbor Deserialize(byte[] data, Type? targetType = null)
        {
            CborReader reader = new(data);
            targetType ??= typeof(CborConstr);

            bool isDefinite = targetType?.GetCustomAttribute<CborDefiniteAttribute>() != null;

            PropertyInfo[]? properties = targetType?.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<CborPropertyAttribute>() != null)
                .OrderBy(p => p.GetCustomAttribute<CborPropertyAttribute>()!.Index)
                .ToArray();

            // Read array start
            CborTag tag = reader.ReadTag();
            CborTag expectedTag = GetTag(targetType?.GetCustomAttribute<CborIndexAttribute>()?.Index ?? 0);

            if (tag != expectedTag)
                throw new InvalidOperationException($"Expected tag {expectedTag}, got {tag}");

            reader.ReadStartArray();

            object[] constructorArgs = new object[properties?.Length ?? 0];

            // Deserialize properties
            for (int i = 0; i < properties?.Length; i++)
            {
                PropertyInfo property = properties[i];
                CborConverterAttribute? converterAttr = property.PropertyType.GetCustomAttribute<CborConverterAttribute>() ?? throw new InvalidOperationException($"Property {property.Name} type must have a CborConverter attribute");
                object converter = Activator.CreateInstance(converterAttr.ConverterType)
                    ?? throw new InvalidOperationException($"Could not create converter instance for {property.Name}");

                // Get deserialize method through reflection
                MethodInfo method = converterAttr.ConverterType.GetMethod("Deserialize")
                    ?? throw new InvalidOperationException($"Converter must implement Deserialize method");

                byte[] encodedValue = [.. reader.ReadEncodedValue().ToArray()];
                object deserializedValue = method.Invoke(converter, [encodedValue])!;
                constructorArgs[i] = deserializedValue;
            }

            reader.ReadEndArray();

            // Create an instance of the custom type using the constructor arguments
            object instance = Activator.CreateInstance(targetType ?? typeof(ICbor), constructorArgs)
                ?? throw new InvalidOperationException($"Could not create instance of type {targetType?.Name}");

            return (ICbor)instance;
        }

        private static CborTag GetTag(int index)
        {
            int finalIndex = index > 6 ? 1280 - 7 : 121;
            return (CborTag)(finalIndex + index);
        }
    }
}
