using System.Collections;
using System.Reflection;

namespace Chrysalis.Cbor.Converters;

public static partial class CborSerializerCore
{
    private static class ValueHandler
    {
        public static object[] GetPropertyValues(object value, int index)
        {
            var type = value.GetType();
            var properties = Registry.GetProperties(type);
            return ExtractPropertyValues(value, type, properties);
        }

        private static object[] ExtractPropertyValues(
            object value,
            Type type,
            PropertyInfo[] properties)
        {
            var values = new object[properties.Length];

            for (var i = 0; i < properties.Length; i++)
            {
                values[i] = GetSinglePropertyValue(type, properties[i].Name, value);
            }

            return values;
        }

        private static object GetSinglePropertyValue(Type type, string propertyName, object value)
        {
            var getter = Registry.GetGetter(type, propertyName);
            return getter.DynamicInvoke(value)
                ?? throw new InvalidOperationException($"Property {propertyName} returned null");
        }

        public static object? ConvertValue(object? value, Type targetType)
        {
            if (value is null) return null;
            return HandleValueConversion(value, targetType);
        }

        private static object? HandleValueConversion(object value, Type targetType)
        {
            if (value.GetType() == targetType) return value;

            return targetType switch
            {
                var t when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                    => ConvertToDictionary(value, t),
                var t when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)
                    => ConvertToList(value, t),
                _ => Convert.ChangeType(value, targetType)
            };
        }

        private static object ConvertToDictionary(object value, Type targetType)
        {
            if (value is not IEnumerable<KeyValuePair<object, object?>> entries)
                throw new InvalidOperationException("Invalid dictionary source");

            var dictTypes = targetType.GetGenericArguments();
            var newDict = CreateGenericDictionary(dictTypes[0], dictTypes[1]);

            PopulateDictionary(entries, newDict);
            return newDict;
        }

        private static void PopulateDictionary(
            IEnumerable<KeyValuePair<object, object?>> entries,
            IDictionary dictionary)
        {
            foreach (var entry in entries)
            {
                if (!dictionary.Contains(entry.Key))
                {
                    dictionary.Add(entry.Key, entry.Value);
                }
            }
        }

        private static IDictionary CreateGenericDictionary(Type keyType, Type valueType)
        {
            var dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
            return (IDictionary)Activator.CreateInstance(dictType)!;
        }

        private static object ConvertToList(object value, Type targetType)
        {
            if (value is not IEnumerable<object> enumerable)
                throw new InvalidOperationException("Invalid list source");

            var elementType = targetType.GetGenericArguments()[0];
            var list = CreateGenericList(elementType);

            PopulateList(enumerable, list);
            return list;
        }

        private static void PopulateList(IEnumerable<object> source, IList list)
        {
            foreach (var item in source)
            {
                list.Add(item);
            }
        }

        private static IList CreateGenericList(Type elementType)
        {
            var listType = typeof(List<>).MakeGenericType(elementType);
            return (IList)Activator.CreateInstance(listType)!;
        }
    }

    // Helper methods for external access if needed
    internal static object[] GetPropertyValues(object value, int index)
        => ValueHandler.GetPropertyValues(value, index);

    internal static object? ConvertValue(object? value, Type targetType)
        => ValueHandler.ConvertValue(value, targetType);
}