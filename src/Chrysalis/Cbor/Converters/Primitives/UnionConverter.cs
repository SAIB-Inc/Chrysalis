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

    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        // CborWriter writer = new();
        // Type type = value.GetType();

        // (object converter, MethodInfo serializeMethod, MethodInfo _) = GetOrCreateConverter(type);
        // if (converter is not null)
        // {
        //     byte[] serializedData = (byte[])serializeMethod.Invoke(converter, [value])!;
        //     writer.WriteEncodedValue(serializedData);
        //     return writer.Encode();
        // }

        // throw new InvalidOperationException($"No converter found for type {type.Name}");
    }

    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        // Type baseType = typeof(T);
        // Type[] concreteTypes = GetConcreteTypes(baseType);

        // // Instead of await, directly block on the returned task:
        // return ParallelDeserializeAsync<T>(data, concreteTypes, baseType).GetAwaiter().GetResult();
        return new object();
    }

    // private static async Task<T> ParallelDeserializeAsync<T>(byte[] data, Type[] concreteTypes, Type baseType) where T : CborBase
    // {
    //     TaskCompletionSource<T> successTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    //     // Create a list of tasks that attempt to deserialize in parallel
    //     Task[] tasks = [.. concreteTypes.Select(concreteType =>
    //     {
    //         if (successTcs.Task.IsCompleted)
    //             return successTcs.Task; // If already succeeded, just return early.

    //         try
    //         {
    //             Type typeToDeserialize = GetClosedGenericType(concreteType, baseType);
    //             T deserializedValue = (T)TryDeserialize(data, typeToDeserialize);
    //             deserializedValue.Raw = data;

    //             // Try to set the result - if another task beat us to it, this will be ignored.
    //             successTcs.TrySetResult(deserializedValue);
    //         }
    //         catch
    //         {
    //             // Ignore failures - if all fail, we handle that after all tasks complete.
    //             // But do not throw here, since we only want to fail if NO type works.
    //         }

    //         return Task.CompletedTask;
    //     })];

    //     // Wait until either one task succeeds (successTcs.Task) or all tasks finish (Task.WhenAll)
    //     Task completed = await Task.WhenAny(successTcs.Task, Task.WhenAll(tasks)).ConfigureAwait(false);

    //     if (completed == successTcs.Task)
    //     {
    //         // We have a successful result
    //         return await successTcs.Task.ConfigureAwait(false);
    //     }

    //     // If we reach here, it means Task.WhenAll completed first and no task succeeded
    //     throw new InvalidOperationException($"Unable to deserialize to any concrete type implementing {baseType.Name}.");
    // }

    // private static Type GetClosedGenericType(Type concreteType, Type baseType)
    // {
    //     if (!baseType.IsGenericType || !concreteType.IsGenericTypeDefinition)
    //     {
    //         return concreteType;
    //     }

    //     Type[] typeArgs = baseType.GetGenericArguments();
    //     return _closedGenericCache.GetOrAdd((concreteType, typeArgs),
    //         key => key.Definition.MakeGenericType(key.Args));
    // }

    // private static Type[] GetConcreteTypes(Type baseType)
    // {
    //     return _concreteTypeCache.GetOrAdd(baseType, type =>
    //     {
    //         Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
    //         return AssemblyUtils.FindConcreteTypes(type, assemblies);
    //     });
    // }

    // private static (object Converter, MethodInfo SerializeMethod, MethodInfo DeserializeMethod) GetOrCreateConverter(Type type)
    // {
    //     return _converterCache.GetOrAdd(type, t =>
    //     {
    //         CborConverterAttribute? converterAttr = t.GetCustomAttribute<CborConverterAttribute>();
    //         if (converterAttr is null || converterAttr.ConverterType == typeof(UnionConverter))
    //         {
    //             return (null!, null!, null!);
    //         }

    //         object converter = Activator.CreateInstance(converterAttr.ConverterType)
    //             ?? throw new InvalidOperationException($"Failed to create converter for {t.Name}");

    //         MethodInfo serializeMethod = converterAttr.ConverterType.GetMethod("Serialize")
    //             ?? throw new InvalidOperationException($"No Serialize method found for {t.Name}");

    //         MethodInfo deserializeMethod = converterAttr.ConverterType.GetMethod("Deserialize")
    //             ?? throw new InvalidOperationException($"No Deserialize method found for {t.Name}");

    //         return (converter, serializeMethod, deserializeMethod);
    //     });
    // }

    // private static CborBase TryDeserialize(byte[] data, Type type)
    // {
    //     (object converter, MethodInfo _, MethodInfo deserializeMethod) = GetOrCreateConverter(type);
    //     if (converter is not null)
    //     {
    //         if (type.ContainsGenericParameters)
    //         {
    //             throw new InvalidOperationException($"Cannot deserialize open generic type {type.Name}");
    //         }

    //         return (CborBase)deserializeMethod.MakeGenericMethod(type).Invoke(converter, [data])!;
    //     }

    //     throw new InvalidOperationException($"No converter found for type {type.Name}");
    // }
}