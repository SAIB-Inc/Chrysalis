using System.Collections.Concurrent;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Converters.Primitives;

public class UnionConverter : ICborConverter
{
    // Cache converter instances and their methods to avoid repeated reflection
    private static readonly ConcurrentDictionary<Type, (object Converter, MethodInfo SerializeMethod, MethodInfo DeserializeMethod)>
        _converterCache = new();

    // Cache concrete types for each base type to avoid repeated assembly scanning
    private static readonly ConcurrentDictionary<Type, Type[]> _concreteTypeCache = new();

    // Cache closed generic types to avoid repeated MakeGenericType calls
    private static readonly ConcurrentDictionary<(Type Definition, Type[] Args), Type> _closedGenericCache = new();

    public byte[] Serialize(CborBase value)
    {
        CborWriter writer = new();
        Type type = value.GetType();

        (object converter, MethodInfo serializeMethod, MethodInfo _) = GetOrCreateConverter(type);
        if (converter is not null)
        {
            byte[] serializedData = (byte[])serializeMethod.Invoke(converter, [value])!;
            writer.WriteEncodedValue(serializedData);
            return writer.Encode();
        }

        throw new InvalidOperationException($"No converter found for type {type.Name}");
    }

    public T Deserialize<T>(byte[] data) where T : CborBase
    {
        Type baseType = typeof(T);
        Type[] concreteTypes = GetConcreteTypes(baseType);

        return ParallelDeserialize<T>(data, concreteTypes, baseType);
    }

    private static T SequentialDeserialize<T>(byte[] data, Type[] concreteTypes, Type baseType) where T : CborBase
    {
        foreach (Type concreteType in concreteTypes)
        {
            try
            {
                Type typeToDeserialize = GetClosedGenericType(concreteType, baseType);
                T deserializedValue = (T)TryDeserialize(data, typeToDeserialize);
                deserializedValue.Raw = data;
                return deserializedValue;
            }
            catch { }
        }

        throw new InvalidOperationException($"Unable to deserialize to any concrete type implementing {baseType.Name}.");
    }

    private static T ParallelDeserialize<T>(byte[] data, Type[] concreteTypes, Type baseType) where T : CborBase
    {
        T? result = null;
        object locker = new();
        bool found = false;

        try
        {
            Parallel.ForEach(
                concreteTypes,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                (concreteType, state) =>
                {
                    if (state.ShouldExitCurrentIteration) return;

                    try
                    {
                        Type typeToDeserialize = GetClosedGenericType(concreteType, baseType);
                        T deserializedValue = (T)TryDeserialize(data, typeToDeserialize);
                        deserializedValue.Raw = data;

                        lock (locker)
                        {
                            if (!found)
                            {
                                found = true;
                                result = deserializedValue;
                                state.Stop();
                            }
                        }
                    }
                    catch
                    {
                        if (state.ShouldExitCurrentIteration) return;
                    }
                });

            if (found && result != null)
            {
                return result;
            }
        }
        catch (AggregateException) { }

        throw new InvalidOperationException($"Unable to deserialize to any concrete type implementing {baseType.Name}.");
    }

    private static Type GetClosedGenericType(Type concreteType, Type baseType)
    {
        if (!baseType.IsGenericType || !concreteType.IsGenericTypeDefinition)
        {
            return concreteType;
        }

        Type[] typeArgs = baseType.GetGenericArguments();
        return _closedGenericCache.GetOrAdd((concreteType, typeArgs),
            key => key.Definition.MakeGenericType(key.Args));
    }

    private static Type[] GetConcreteTypes(Type baseType)
    {
        return _concreteTypeCache.GetOrAdd(baseType, type =>
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            return AssemblyUtils.FindConcreteTypes(type, assemblies);
        });
    }

    private static (object Converter, MethodInfo SerializeMethod, MethodInfo DeserializeMethod) GetOrCreateConverter(Type type)
    {
        return _converterCache.GetOrAdd(type, t =>
        {
            CborConverterAttribute? converterAttr = t.GetCustomAttribute<CborConverterAttribute>();
            if (converterAttr is null || converterAttr.ConverterType == typeof(UnionConverter))
            {
                return (null!, null!, null!);
            }

            object converter = Activator.CreateInstance(converterAttr.ConverterType)
                ?? throw new InvalidOperationException($"Failed to create converter for {t.Name}");

            MethodInfo serializeMethod = converterAttr.ConverterType.GetMethod("Serialize")
                ?? throw new InvalidOperationException($"No Serialize method found for {t.Name}");

            MethodInfo deserializeMethod = converterAttr.ConverterType.GetMethod("Deserialize")
                ?? throw new InvalidOperationException($"No Deserialize method found for {t.Name}");

            return (converter, serializeMethod, deserializeMethod);
        });
    }

    private static CborBase TryDeserialize(byte[] data, Type type)
    {
        (object converter, MethodInfo _, MethodInfo deserializeMethod) = GetOrCreateConverter(type);
        if (converter is not null)
        {
            if (type.ContainsGenericParameters)
            {
                throw new InvalidOperationException($"Cannot deserialize open generic type {type.Name}");
            }

            return (CborBase)deserializeMethod.MakeGenericMethod(type).Invoke(converter, [data])!;
        }

        throw new InvalidOperationException($"No converter found for type {type.Name}");
    }
}