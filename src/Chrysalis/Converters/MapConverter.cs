using System.Collections;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class MapConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : Cbor
    {
        CborReader reader = new(data);
        reader.ReadStartMap();

        Type targetType = typeof(T);
        ConstructorInfo constructor = targetType.GetConstructors().FirstOrDefault() ?? throw new InvalidOperationException($"Type {targetType.Name} must have a constructor.");

        // Determine the key and value types of the dictionary
        Type dictionaryType = constructor.GetParameters()[0].ParameterType;
        Type[] genericArgs = dictionaryType.GetGenericArguments();
        if (genericArgs.Length != 2 || !typeof(Cbor).IsAssignableFrom(genericArgs[0]) || !typeof(Cbor).IsAssignableFrom(genericArgs[1]))
        {
            throw new InvalidOperationException("The dictionary's key and value types must inherit from Cbor.");
        }

        Type keyType = genericArgs[0];
        Type valueType = genericArgs[1];

        // Create a dictionary for the entries
        IDictionary dictionary = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType))!;

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            byte[] keyData = reader.ReadEncodedValue().ToArray();
            byte[] valueData = reader.ReadEncodedValue().ToArray();

            MethodInfo deserializeMethod = typeof(CborSerializer).GetMethod(nameof(CborSerializer.Deserialize))!;

            object? key = deserializeMethod.MakeGenericMethod(keyType).Invoke(null, [keyData]);
            object? value = deserializeMethod.MakeGenericMethod(valueType).Invoke(null, [valueData]);

            if (key is not Cbor cborKey || value is not Cbor cborValue)
                throw new InvalidOperationException("Dictionary keys and values must be of type Cbor.");

            dictionary.Add(key, value);
        }

        reader.ReadEndMap();

        // Create the Cbor instance
        return (T)constructor.Invoke([dictionary]);
    }

    public byte[] Serialize(Cbor data)
    {
        Type type = data.GetType();

        // Extract the dictionary property
        PropertyInfo dictionaryProperty = type.GetProperties().FirstOrDefault(p => typeof(IDictionary).IsAssignableFrom(p.PropertyType))
            ?? throw new InvalidOperationException("No IDictionary property found for the CborMap.");

        // Get the dictionary
        if (dictionaryProperty.GetValue(data) is not IDictionary dictionary) throw new InvalidOperationException("The dictionary property is null.");

        CborWriter writer = new();
        writer.WriteStartMap(dictionary.Count);

        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is not Cbor cborKey) throw new InvalidOperationException("Dictionary keys must be of type Cbor.");
            if (entry.Value is not Cbor cborValue) throw new InvalidOperationException("Dictionary values must be of type Cbor.");

            writer.WriteEncodedValue(CborSerializer.Serialize(cborKey));
            writer.WriteEncodedValue(CborSerializer.Serialize(cborValue));
        }

        writer.WriteEndMap();
        return writer.Encode();
    }
}