using System.Collections;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;

public class MapConverter : ICborConverter
{
    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        Type activatorType = options?.ActivatorType!;
        ConstructorInfo ctor = activatorType.GetConstructors().FirstOrDefault()!;
        ParameterInfo[] parameters = ctor.GetParameters();
        ParameterInfo dictParam = parameters.First(p =>
            p.ParameterType.IsGenericType &&
            p.ParameterType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
        );
        Type dictionaryType = dictParam.ParameterType;
        Type[] genericArgs = dictionaryType.GetGenericArguments();

        // Collect key-value pairs
        var entries = new List<KeyValuePair<object, object?>>();

        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            // Read encoded values
            byte[] keyData = reader.ReadEncodedValue().ToArray();
            byte[] valueData = reader.ReadEncodedValue().ToArray();

            // Let CborSerializer handle deserialization
            object key = CborSerializer.Deserialize(new CborReader(keyData),
                CborSerializer.GetOptions(genericArgs[0]))!;
            object? value = CborSerializer.Deserialize(new CborReader(valueData),
                CborSerializer.GetOptions(genericArgs[1]));

            entries.Add(new KeyValuePair<object, object?>(key, value));
        }
        reader.ReadEndMap();

        return entries;
    }

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        // Get the actual type of the value.
        Type activatorType = value.GetType();

        // Find the dictionary property.
        PropertyInfo? dictProp = activatorType.GetProperties()
            .FirstOrDefault(p => IsDictionaryType(p.PropertyType));
        ConstructorInfo? ctor = activatorType.GetConstructors()
            .FirstOrDefault(c =>
            {
                ParameterInfo[] ps = c.GetParameters();
                return ps.Length == 1 && IsDictionaryType(ps[0].ParameterType);
            });

        IDictionary dictionary;
        if (dictProp != null)
        {
            dictionary = (IDictionary)dictProp.GetValue(value)!;
        }
        else if (ctor != null)
        {
            // If using the constructor approach, assume the value itself is the dictionary.
            dictionary = (IDictionary)value;
        }
        else
        {
            throw new InvalidOperationException($"Could not find dictionary in {activatorType.Name}");
        }

        writer.WriteStartMap(options?.IsDefinite == true ? dictionary.Count : null);
        foreach (DictionaryEntry entry in dictionary)
        {
            CborSerializer.Serialize(writer, entry.Key);
            CborSerializer.Serialize(writer, entry.Value);
        }
        writer.WriteEndMap();
    }

    private bool IsDictionaryType(Type type)
    {
        if (!type.IsGenericType) return false;
        Type genericDef = type.GetGenericTypeDefinition();
        return genericDef == typeof(Dictionary<,>) || genericDef == typeof(IReadOnlyDictionary<,>);
    }
}