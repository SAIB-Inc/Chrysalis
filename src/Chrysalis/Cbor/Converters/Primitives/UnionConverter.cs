using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class UnionConverter : ICborConverter
{
    public byte[] Serialize(CborBase value)
    {
        CborWriter writer = new();
        Type type = value.GetType();

        // Get the converter for the concrete type
        CborConverterAttribute? converterAttr = type.GetCustomAttribute<CborConverterAttribute>();
        if (converterAttr is not null && converterAttr.ConverterType != typeof(UnionConverter))
        {
            // Use the concrete type's converter
            object converterInstance = Activator.CreateInstance(converterAttr.ConverterType)
                ?? throw new InvalidOperationException($"Failed to create converter for {type.Name}");

            MethodInfo serializeMethod = converterAttr.ConverterType.GetMethod("Serialize")
                ?? throw new InvalidOperationException($"No Serialize method found for {type.Name}");

            byte[] serializedData = (byte[])serializeMethod.Invoke(converterInstance, [value])!;
            writer.WriteEncodedValue(serializedData);
            return writer.Encode();
        }

        throw new InvalidOperationException($"No converter found for type {type.Name}");
    }

    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        Type baseType = typeof(T);

        // Get all concrete types that implement the interface or base class
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        Type[] concreteTypes = AssemblyUtils.FindConcreteTypes(baseType, assemblies);

        foreach (Type concreteType in concreteTypes)
        {
            try
            {
                // If the base type is generic (like Option<T>) and concrete type is generic (like Some<T>)
                // We need to close the concrete type with the type arguments from T
                Type typeToDeserialize = concreteType;
                if (baseType.IsGenericType && concreteType.IsGenericTypeDefinition)
                {
                    typeToDeserialize = concreteType.MakeGenericType(baseType.GetGenericArguments());
                }

                CborBase deserializedValue = TryDeserialize(data, typeToDeserialize);
                if (deserializedValue != null)
                {
                    return (T)deserializedValue;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to deserialize as {concreteType.Name}: {ex.Message}");
            }
        }

        throw new InvalidOperationException($"Unable to deserialize to any concrete type implementing {baseType.Name}.");
    }

    private static CborBase TryDeserialize(byte[] data, Type type)
    {
        // Check if the type has a specific converter
        CborConverterAttribute? converterAttr = type.GetCustomAttribute<CborConverterAttribute>();
        if (converterAttr != null)
        {
            object converterInstance = Activator.CreateInstance(converterAttr.ConverterType)
                ?? throw new InvalidOperationException($"Failed to create converter for {type.Name}");

            MethodInfo deserializeMethod = converterAttr.ConverterType.GetMethod("Deserialize")
                ?? throw new InvalidOperationException($"No Deserialize method found for {type.Name}");

            // At this point, type should be a closed generic type if it was generic
            if (type.ContainsGenericParameters)
            {
                throw new InvalidOperationException($"Cannot deserialize open generic type {type.Name}");
            }

            return (CborBase)deserializeMethod.MakeGenericMethod(type).Invoke(converterInstance, [data])!;
        }

        throw new InvalidOperationException($"No converter found for type {type.Name}");
    }
}