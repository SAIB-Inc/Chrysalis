using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Attributes;
using Chrysalis.Types;

namespace Chrysalis.Converters
{
    public class ConstrConverter : ICborConverter
    {
        public T Deserialize<T>(byte[] data) where T : Cbor
        {
            CborReader reader = new(data);

            Type targetType = typeof(T);
            bool isDefinite = targetType.GetCustomAttribute<CborDefiniteAttribute>() != null;

            // Use the helper to get constructor parameters or properties
            List<(int? Index, string Name, Type Type)> parametersOrProperties = GetCborPropertiesOrParameters(targetType).ToList();

            // Read the tag and validate it
            CborTag tag = reader.ReadTag();
            CborTag expectedTag = GetTag(targetType.GetCustomAttribute<CborIndexAttribute>()?.Index ?? 0);

            if (tag != expectedTag)
            {
                throw new InvalidOperationException($"Expected tag {expectedTag}, got {tag}");
            }

            // Read array start
            reader.ReadStartArray();

            // Prepare arguments for constructor
            object?[] constructorArgs = new object[parametersOrProperties.Count];

            for (int i = 0; i < parametersOrProperties.Count; i++)
            {
                (int? Index, string Name, Type ParameterType) = parametersOrProperties[i];

                // Deserialize the value
                byte[] encodedValue = reader.ReadEncodedValue().ToArray();
                MethodInfo deserializeMethod = typeof(CborSerializer).GetMethod(nameof(CborSerializer.Deserialize))!;
                object? deserializedValue = deserializeMethod.MakeGenericMethod(ParameterType)
                    .Invoke(null, [encodedValue]);

                constructorArgs[i] = deserializedValue;
            }

            reader.ReadEndArray();

            // Create an instance using the resolved constructor arguments
            T instance = (T)Activator.CreateInstance(targetType, constructorArgs)!;
            instance.Raw = data;

            return instance;
        }


        public byte[] Serialize(Cbor value)
        {
            Type type = value.GetType();
            int index = type.GetCustomAttribute<CborIndexAttribute>()?.Index ?? 0;

            bool isDefinite = type.GetCustomAttribute<CborDefiniteAttribute>() != null;

            PropertyInfo[] properties = [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<CborPropertyAttribute>() != null)
                .OrderBy(p => p.GetCustomAttribute<CborPropertyAttribute>()!.Index)];

            CborWriter writer = new();

            // Write the tag
            writer.WriteTag(GetTag(index));

            // Write array start
            writer.WriteStartArray(isDefinite ? properties.Length : null);

            // Serialize properties
            foreach (PropertyInfo property in properties)
            {
                object? propertyValue = property.GetValue(value);
                byte[] serialized = CborSerializer.Serialize((Cbor)propertyValue!);
                writer.WriteEncodedValue(serialized);
            }

            writer.WriteEndArray();
            return writer.Encode();
        }

        private static CborTag GetTag(int index)
        {
            int finalIndex = index > 6 ? 1280 - 7 : 121;
            return (CborTag)(finalIndex + index);
        }


        private static IEnumerable<(int? Index, string Name, Type Type)> GetCborPropertiesOrParameters(Type type)
        {
            // First try to get constructor parameters
            var constructor = type.GetConstructors().FirstOrDefault();
            if (constructor != null)
            {
                var parameters = constructor.GetParameters()
                    .Select(p =>
                    {
                        var attr = p.GetCustomAttribute<CborPropertyAttribute>();
                        return (attr?.Index, Name: p.Name ?? "", Type: p.ParameterType);
                    });

                // Filter out Raw property using named field
                return parameters.Where(p => p.Name != "Raw");
            }

            // Fallback to properties if no constructor
            return type.GetProperties()
                .Where(p =>
                    p.CanRead &&
                    p.CanWrite &&
                    p.Name != "Raw" &&
                    p.GetCustomAttribute<CborPropertyAttribute>() != null)
                .Select(p =>
                {
                    var attr = p.GetCustomAttribute<CborPropertyAttribute>();
                    return (attr?.Index, p.Name, Type: p.PropertyType);
                });
        }
    }
}