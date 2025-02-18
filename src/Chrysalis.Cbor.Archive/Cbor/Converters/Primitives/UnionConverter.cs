
using System.Collections.Concurrent;
using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters.Primitives;


public class UnionConverter : ICborConverter
{
    public void Serialize(CborWriter writer, object value, CborOptions? options = null)
    {
        CborSerializer.Serialize(writer, value);
    }

    public object Deserialize(CborReader reader, CborOptions? options = null)
    {
        byte[] encodedValue = reader.ReadEncodedValue(disableConformanceModeChecks: true).ToArray();
        Type baseType = options?.ActivatorType ??
            throw new InvalidOperationException("Union type not specified in options");
        IEnumerable<Type> concreteTypes = options?.UnionTypes ??
            throw new InvalidOperationException("Union types not specified in options");

        if (baseType.IsGenericType && !baseType.IsGenericTypeDefinition)
        {
            Type[] typeArgs = baseType.GetGenericArguments();
            concreteTypes = concreteTypes
                .Select(t => t.IsGenericTypeDefinition ? t.MakeGenericType(typeArgs) : t);
        }

        foreach (Type concreteType in concreteTypes)
        {
            try
            {
                object? result = CborSerializer.TryDeserialize(encodedValue, concreteType);
                if (result != null)
                {
                    if (result.ToString() == "LoveForCrypto1of25")
                    {
                        Console.WriteLine("sda");
                    }
                    options.ActivatorType = concreteType;
                    return result;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                // Continue to next type if this one fails
            }
        }

        throw new InvalidOperationException($"Unable to deserialize to any concrete type implementing {baseType.Name}");

        // public object Deserialize(CborReader reader, CborOptions? options = null)
        // {
        //     byte[] encodedValue = reader.ReadEncodedValue().ToArray();

        //     Type baseType = options?.ActivatorType ??
        //         throw new InvalidOperationException("Union type not specified in options");
        //     IEnumerable<Type> concreteTypes = options?.UnionTypes ??
        //         throw new InvalidOperationException("Union types not specified in options");

        //     if (Convert.ToHexString(encodedValue) == "8201D818583CD87A9FD8799F581CE33BB9C5E634BC5BE187EABA0D94725EF71505D4A4CA6E0FED0B2E6DD8799FD8799F1A0005B9801B0000019096A7E9B2FFFFFFFF")
        //     {
        //         Console.WriteLine("sda");
        //     }

        //     if (baseType.IsGenericType && !baseType.IsGenericTypeDefinition)
        //     {
        //         Type[] typeArgs = baseType.GetGenericArguments();
        //         concreteTypes = concreteTypes
        //             .Select(t => t.IsGenericTypeDefinition ? t.MakeGenericType(typeArgs) : t)
        //             .ToList();
        //     }

        //     // Try first type synchronously
        //     foreach (var type in concreteTypes.Take(1))
        //     {
        //         try
        //         {
        //             object? result = CborSerializer.TryDeserialize(encodedValue, type);
        //             if (result != null)
        //             {
        //                 if (options != null)
        //                 {
        //                     typeof(CborOptions).GetProperty(nameof(CborOptions.ActivatorType))!
        //                         .SetValue(options, type);
        //                 }
        //                 return result;
        //             }
        //         }
        //         catch { }
        //     }

        //     // Process remaining types in parallel
        //     var remainingTypes = concreteTypes.Skip(1).ToList();
        //     if (!remainingTypes.Any())
        //     {
        //         throw new InvalidOperationException($"Unable to deserialize to any concrete type implementing {baseType.Name}");
        //     }

        //     var tasks = remainingTypes.Select(type =>
        //         Task.Run(() =>
        //         {
        //             try
        //             {
        //                 var localOptions = new CborOptions(
        //                     ConverterType: options!.ConverterType,
        //                     ActivatorType: type,
        //                     PropertyIndexTypes: options.PropertyIndexTypes,
        //                     PropertyNameTypes: options.PropertyNameTypes
        //                 );
        //                 return (Type: type, Result: CborSerializer.TryDeserialize(encodedValue, type));
        //             }
        //             catch
        //             {
        //                 return (Type: type, Result: null);
        //             }
        //         })).ToList();

        //     while (tasks.Any())
        //     {
        //         var completedTask = Task.WhenAny(tasks).Result;
        //         tasks.Remove(completedTask);

        //         var (type, result) = completedTask.Result;
        //         if (result != null)
        //         {
        //             if (options != null)
        //             {
        //                 typeof(CborOptions).GetProperty(nameof(CborOptions.ActivatorType))!
        //                     .SetValue(options, type);
        //             }
        //             return result;
        //         }
        //     }

        //     throw new InvalidOperationException($"Unable to deserialize to any concrete type implementing {baseType.Name}");
        // }
    }
}