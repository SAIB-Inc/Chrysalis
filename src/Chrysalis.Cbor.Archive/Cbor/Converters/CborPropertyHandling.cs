using System.Reflection;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters;

public static partial class CborSerializerCore
{

    private static class PropertyHandler
    {
        public static Dictionary<object, object> GetPropertyMappings(object value)
        {
            var type = value.GetType();
            var properties = Registry.GetProperties(type);
            return CreatePropertyDictionary(type, properties, value);
        }

        private static Dictionary<object, object> CreatePropertyDictionary(
            Type type,
            PropertyInfo[] properties,
            object value)
        {
            var mappings = new Dictionary<object, object>();
            foreach (var property in properties)
            {
                AddPropertyToMappings(type, property, value, mappings);
            }
            return mappings;
        }

        private static void AddPropertyToMappings(
            Type type,
            PropertyInfo property,
            object value,
            Dictionary<object, object> mappings)
        {
            var getter = Registry.GetGetter(type, property.Name);
            var propertyValue = getter.DynamicInvoke(value);

            if (propertyValue is not null)
            {
                var key = GetPropertyKey(type, property.Name);
                mappings[key] = propertyValue;
            }
        }

        private static object GetPropertyKey(Type type, string propertyName)
        {
            var options = Registry.GetOptions(type);
            var properties = Registry.GetProperties(type);

            return DeterminePropertyKey(options, properties, propertyName);
        }

        private static object DeterminePropertyKey(
            CborOptions? options,
            PropertyInfo[] properties,
            string propertyName)
        {
            // Check for named property mapping
            if (options?.PropertyNameTypes?.TryGetValue(propertyName, out _) == true)
            {
                return propertyName;
            }

            // Check for indexed property mapping
            if (options?.PropertyIndexTypes is not null)
            {
                var indexedProperty = FindIndexedProperty(options.PropertyIndexTypes, properties, propertyName);
                if (indexedProperty.HasValue)
                {
                    return indexedProperty.Value;
                }
            }

            // Default to lowercase property name
            return propertyName.ToLowerInvariant();
        }

        private static int? FindIndexedProperty(
            Dictionary<int, Type> indexTypes,
            PropertyInfo[] properties,
            string propertyName)
        {
            foreach (var (index, _) in indexTypes)
            {
                if (properties[index].Name == propertyName)
                {
                    return index;
                }
            }
            return null;
        }
    }

    // Helper method for external access if needed
    internal static Dictionary<object, object> GetPropertyMappings(object value)
        => PropertyHandler.GetPropertyMappings(value);
}