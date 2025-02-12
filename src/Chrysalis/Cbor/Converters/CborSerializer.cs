using System.Collections.Concurrent;
using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cache;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters;

public static class CborSerializer
{
    private static ChrysalisRegistry? _registry;
    private static ChrysalisRegistry Registry
    {
        get
        {
            if (_registry == null)
            {
                _registry = new ChrysalisRegistry();
                _registry.InitializeRegistry();
            }
            return _registry;
        }
        set => _registry = value;
    }

    public static byte[] Serialize<T>(T value) where T : CborBase
    {
        throw new NotImplementedException();
    }

    public static T Deserialize<T>(byte[] data) where T : CborBase
    {
        var test = Registry.Constructors;
        throw new NotImplementedException();
    }

    // Internal API for converters
    internal static object Deserialize(CborReader reader)
    {
        throw new NotImplementedException();
    }

    internal static void Serialize(CborWriter writer, object? value)
    {
        throw new NotImplementedException();
    }

    internal static object[] GetPropertyValues(object value, int index)
    {
        Type type = value.GetType();
        PropertyInfo[] properties = Registry.Properties[type];
        object[] values = new object[properties.Length];
        for (int i = 0; i < properties.Length; i++)
        {
            Delegate getter = Registry.Getters[(type, properties[i].Name)];
            values[i] = getter.DynamicInvoke(value)!;
        }
        return values;
    }

    internal static object[] GetSortedProperties(object value)
    {
        Type type = value.GetType();
        PropertyInfo[] properties = Registry.Properties[type];
        object[] values = new object[properties.Length];
        for (int i = 0; i < properties.Length; i++)
        {
            Delegate getter = Registry.Getters[(type, properties[i].Name)];
            object? propValue = getter.DynamicInvoke(value);
            if (propValue != null)
            {
                values[i] = propValue;
            }
        }
        return values;
    }

    internal static Dictionary<object, object> GetPropertyMappings(object value)
    {
        Type type = value.GetType();
        PropertyInfo[] properties = Registry.Properties[type];
        Dictionary<object, object> mappings = new Dictionary<object, object>();

        foreach (PropertyInfo prop in properties)
        {
            Delegate getter = Registry.Getters[(type, prop.Name)];
            object? propValue = getter.DynamicInvoke(value);
            if (propValue != null)
            {
                object key = GetPropertyKey(type, prop.Name);
                mappings[key] = propValue;
            }
        }

        return mappings;
    }

    private static object GetPropertyKey(Type type, string propertyName)
    {
        CborOptions? options = Registry.Options[type];
        PropertyInfo[] properties = Registry.Properties[type];
        PropertyInfo property = properties.First(p => p.Name == propertyName);
        if (options?.PropertyNames?.Any() == true)
        {
            // Use string name if available
            string customName = options.PropertyNames
                .FirstOrDefault(n => n == propertyName) ?? propertyName;
            return customName;
        }
        if (options?.PropertyIndices?.Any() == true)
        {
            int index = options.PropertyIndices
                .ElementAtOrDefault(Array.IndexOf(properties, property));
            return index;
        }
        return propertyName.ToLowerInvariant();
    }
}