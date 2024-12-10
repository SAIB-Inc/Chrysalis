using System.Collections;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class CustomConstrConverter : ICborConverter
{
    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        CborReader reader = new(data);
        Type targetType = typeof(T);

        // Read the tag
        int tag = (int)reader.ReadTag();

        // Read array start
        reader.ReadStartArray();

        // Get constructor parameter info
        List<(int? Index, string Name, Type Type)> parameters = AssemblyUtils.GetCborPropertiesOrParameters(targetType).ToList();

        if (parameters.Count != 1)
            throw new InvalidOperationException($"Type {targetType.Name} must have exactly one constructor parameter");

        // Check if parameter is List<T> where T : CborBase
        Type paramType = parameters[0].Type;
        if (!paramType.IsGenericType || paramType.GetGenericTypeDefinition() != typeof(List<>))
            throw new InvalidOperationException($"Type {targetType.Name} must have a constructor parameter of type List<T> where T : CborBase");

        Type elementType = paramType.GetGenericArguments()[0];
        if (!typeof(CborBase).IsAssignableFrom(elementType))
            throw new InvalidOperationException($"List element type must inherit from CborBase");

        // Create the list
        IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;

        // Read all elements
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            byte[] encodedValue = reader.ReadEncodedValue().ToArray();
            MethodInfo deserializeMethod = typeof(CborSerializer).GetMethod(nameof(CborSerializer.Deserialize))!;
            object item = deserializeMethod.MakeGenericMethod(elementType).Invoke(null, [encodedValue])!;
            list.Add(item);
        }

        reader.ReadEndArray();

        // Create instance
        ConstructorInfo constructor = targetType.GetConstructors().First();
        T instance = (T)constructor.Invoke([list]);
        instance.Raw = data;
        return instance;
    }

    public byte[] Serialize(CborBase value)
    {
        Type type = value.GetType();
        CborWriter writer = new();

        // First check for tag attribute
        CborTagAttribute? tagAttr = type.GetCustomAttribute<CborTagAttribute>();
        if (tagAttr != null)
        {
            writer.WriteTag((CborTag)tagAttr.Tag);
        }
        else
        {
            // Fall back to Tag property if it exists
            PropertyInfo? tagProperty = type.GetProperty("Tag");
            if (tagProperty != null)
            {
                int tag = (int?)tagProperty.GetValue(value)
                    ?? throw new InvalidOperationException("Tag property cannot be null");
                writer.WriteTag((CborTag)tag);
            }
            else
            {
                throw new InvalidOperationException("Type must either have a CborTag attribute or a Tag property");
            }
        }

        // Get the List<T> property
        PropertyInfo listProperty = type.GetProperties()
            .FirstOrDefault(p => p.PropertyType.IsGenericType &&
                               p.PropertyType.GetGenericTypeDefinition() == typeof(List<>) &&
                               typeof(CborBase).IsAssignableFrom(p.PropertyType.GetGenericArguments()[0]))
            ?? throw new InvalidOperationException("Type must have a property of type List<T> where T : CborBase");

        if (listProperty.GetValue(value) is not IList list)
            throw new InvalidOperationException("List property cannot be null");

        bool isDefinite = type.GetCustomAttribute<CborDefiniteAttribute>() != null;
        writer.WriteStartArray(isDefinite ? list.Count : null);

        foreach (object? item in list)
        {
            if (item is not CborBase cborItem)
                throw new InvalidOperationException("List elements must be of type CborBase");

            byte[] serialized = CborSerializer.Serialize(cborItem);
            writer.WriteEncodedValue(serialized);
        }

        writer.WriteEndArray();
        return writer.Encode();
    }
}