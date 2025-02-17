using System.Collections;
using System.Reflection;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters;

public static partial class CborSerializerCore
{
    private static class ConstructorHandler
    {
        public static object CreateInstance(Type type, object? value, CborOptions options)
        {
            var ctor = GetAppropriateConstructor(type);
            return InstantiateObject(type, ctor, value, options);
        }

        private static ConstructorInfo GetAppropriateConstructor(Type type)
        {
            var bindingFlags = GetBindingFlags(type);
            return FindConstructor(type, bindingFlags);
        }

        private static BindingFlags GetBindingFlags(Type type)
            => type.IsGenericType && !type.IsGenericTypeDefinition
                ? BindingFlags.Public | BindingFlags.Instance
                : BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        private static ConstructorInfo FindConstructor(Type type, BindingFlags flags)
            => type.GetConstructors(flags)
                   .OrderByDescending(c => c.GetParameters().Length)
                   .FirstOrDefault()
                ?? throw new InvalidOperationException($"No suitable constructor found for {type.FullName}");

        private static object InstantiateObject(
            Type type,
            ConstructorInfo ctor,
            object? value,
            CborOptions options)
        {
            var parameters = ctor.GetParameters();

            return parameters.Length switch
            {
                0 => Activator.CreateInstance(type)!,
                1 => HandleSingleParameter(type, ctor, value, parameters[0]),
                _ => HandleMultipleParameters(type, ctor, value, parameters)
            };
        }

        private static object HandleSingleParameter(
            Type type,
            ConstructorInfo ctor,
            object? value,
            ParameterInfo parameter)
        {
            var paramType = parameter.ParameterType;

            if (IsCollectionType(paramType))
            {
                return HandleCollectionParameter(type, value, paramType);
            }

            return Activator.CreateInstance(type, value)!;
        }

        private static bool IsCollectionType(Type type)
            => type.IsGenericType && (
                type.GetGenericTypeDefinition() == typeof(Dictionary<,>) ||
                type.GetGenericTypeDefinition() == typeof(List<>)
            );

        private static object HandleCollectionParameter(Type type, object? value, Type paramType)
        {
            if (IsDictionaryType(paramType))
            {
                return HandleDictionaryParameter(type, value, paramType);
            }

            if (IsListType(paramType))
            {
                return HandleListParameter(type, value, paramType);
            }

            throw new InvalidOperationException($"Unsupported collection type: {paramType}");
        }

        private static bool IsDictionaryType(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);

        private static bool IsListType(Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);

        private static object HandleDictionaryParameter(Type type, object? value, Type paramType)
        {
            if (value is not List<KeyValuePair<object, object?>> entries)
                throw new InvalidOperationException("Invalid dictionary data");

            var dictTypes = paramType.GetGenericArguments();
            var dict = CreateTypedDictionary(dictTypes[0], dictTypes[1], entries);

            return Activator.CreateInstance(type, dict)!;
        }

        private static object HandleListParameter(Type type, object? value, Type paramType)
        {
            if (value is not IEnumerable<object> items)
                throw new InvalidOperationException("Invalid list data");

            var elementType = paramType.GetGenericArguments()[0];
            var list = CreateTypedList(elementType, items);

            return Activator.CreateInstance(type, list)!;
        }

        private static object HandleMultipleParameters(
            Type type,
            ConstructorInfo ctor,
            object? value,
            ParameterInfo[] parameters)
        {
            var args = PrepareConstructorArguments(value, parameters.Length);
            return Activator.CreateInstance(type, args)!;
        }

        private static object?[] PrepareConstructorArguments(object? value, int paramCount)
        {
            var args = new object?[paramCount];

            if (value is IEnumerable<object> list)
            {
                var values = list.ToList();
                Array.Copy(values.ToArray(), args, Math.Min(values.Count, paramCount));
            }

            return args;
        }

        private static IDictionary CreateTypedDictionary(
       Type keyType,
       Type valueType,
       List<KeyValuePair<object, object?>> entries)
        {
            var dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
            var dict = (IDictionary)Activator.CreateInstance(dictType)!;

            foreach (var entry in entries)
            {
                if (!dict.Contains(entry.Key))
                {
                    var convertedKey = ConvertValue(entry.Key, keyType);
                    var convertedValue = ConvertValue(entry.Value, valueType);
                    dict.Add(convertedKey!, convertedValue);
                }
            }

            return dict;
        }

        private static IList CreateTypedList(
            Type elementType,
            IEnumerable<object> items)
        {
            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = (IList)Activator.CreateInstance(listType)!;

            foreach (var item in items)
            {
                var convertedItem = ConvertValue(item, elementType);
                list.Add(convertedItem);
            }

            return list;
        }

        private static object? ConvertValue(object? value, Type targetType)
        {
            if (value is null) return null;
            if (value.GetType() == targetType) return value;

            try
            {
                return Convert.ChangeType(value, targetType);
            }
            catch (InvalidCastException)
            {
                throw new InvalidOperationException(
                    $"Cannot convert value of type {value.GetType()} to {targetType}");
            }
        }
    }

    // Helper method for external access if needed
    internal static object CreateInstance(Type type, object? value, CborOptions options)
        => ConstructorHandler.CreateInstance(type, value, options);
}