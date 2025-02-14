
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
        byte[] encodedValue = reader.ReadEncodedValue().ToArray();

        if (Convert.ToHexString(encodedValue) == "821A001E8480A1581CB7EF0CFB14F588740D02CE8A53879FF96AB03C7B21EC1C459AC2BDDAA15048617368477561726469616E3137333201")
        {
            Console.WriteLine("Deserializing UnionConverter");
        }
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

        // TaskCompletionSource<object> tcs = new();
        // Parallel.ForEach(concreteTypes, type =>
        // {
        //     if (tcs.Task.IsCompleted) return;
        //     try
        //     {
        //         object? result = CborSerializer.TryDeserialize(encodedValue, type);
        //         if (result != null)
        //         {
        //             tcs.TrySetResult(result);
        //         }
        //     }
        //     catch
        //     {
        //         // Continue to next type if this one fails
        //     }
        // });

        // if (tcs.Task.IsCompleted)
        // {
        //     return tcs.Task.Result;
        // }
        throw new InvalidOperationException($"Unable to deserialize to any concrete type implementing {baseType.Name}");
    }
}