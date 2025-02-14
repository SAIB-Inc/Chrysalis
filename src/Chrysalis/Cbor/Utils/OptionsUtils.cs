using System.Reflection;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Utils;

internal static class OptionsUtils
{
    public static CborOptions CreateOptions(Type type, Dictionary<Type, List<Type>> unionTypes)
    {
        Dictionary<string, Type> propertyNameTypes = [];
        Dictionary<int, Type> propertyIndexTypes = [];
        Type? converterType = null;
        bool? isDefinite = null;
        int? index = null;
        int? size = null;
        int? tag = null;

        // Walk up inheritance chain to gather attributes
        Type? currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            // Get converter attribute first
            var converterAttr = currentType.GetCustomAttribute<CborConverterAttribute>();
            if (converterAttr != null && converterType == null)
            {
                converterType = converterAttr.ConverterType;
            }

            // Get other attributes
            var indexAttr = currentType.GetCustomAttribute<CborIndexAttribute>();
            if (indexAttr != null && index == null)
            {
                index = indexAttr.Index;
            }

            var tagAttr = currentType.GetCustomAttribute<CborTagAttribute>();
            if (tagAttr != null && tag == null)
            {
                tag = tagAttr.Tag;
            }

            if (isDefinite == null)
            {
                if (currentType.GetCustomAttribute<CborDefiniteAttribute>() != null)
                    isDefinite = true;
                else if (currentType.GetCustomAttribute<CborIndefiniteAttribute>() != null)
                    isDefinite = false;
            }

            // Process properties
            foreach (var prop in currentType.GetProperties())
            {
                var attr = prop.GetCustomAttribute<CborPropertyAttribute>();
                if (attr != null)
                {
                    if (attr.PropertyName != null)
                        propertyNameTypes[attr.PropertyName] = prop.PropertyType;
                    if (attr.Index.HasValue)
                        propertyIndexTypes[attr.Index.Value] = prop.PropertyType;
                }
            }

            // Process constructor parameters
            foreach (var ctor in currentType.GetConstructors())
            {
                foreach (var param in ctor.GetParameters())
                {
                    var attr = param.GetCustomAttribute<CborPropertyAttribute>();
                    if (attr != null)
                    {
                        if (attr.PropertyName != null)
                            propertyNameTypes[attr.PropertyName] = param.ParameterType;
                        if (attr.Index.HasValue)
                            propertyIndexTypes[attr.Index.Value] = param.ParameterType;
                    }
                }
            }

            currentType = currentType.BaseType;
        }

        return new CborOptions(
            Index: index,
            ConverterType: converterType,
            IsDefinite: isDefinite,
            IsUnion: converterType == typeof(UnionConverter),
            ActivatorType: type,
            Size: size,
            Tag: tag,
            PropertyNameTypes: propertyNameTypes,
            PropertyIndexTypes: propertyIndexTypes,
            UnionTypes: unionTypes.GetValueOrDefault(type)
        );
    }

    private static bool? GetDefiniteAttribute(Type type) =>
        type.GetCustomAttribute<CborDefiniteAttribute>() != null ? true :
        type.GetCustomAttribute<CborIndefiniteAttribute>() != null ? false : null;

    private static Dictionary<string, Type> CollectPropertyNames(Type type) =>
        type.GetProperties()
            .Select(p => (Prop: p, Attr: p.GetCustomAttribute<CborPropertyAttribute>()))
            .Where(x => x.Attr?.PropertyName != null)
            .ToDictionary(x => x.Attr!.PropertyName!, x => x.Prop.PropertyType);

    private static Dictionary<int, Type> CollectPropertyIndices(Type type) =>
        type.GetProperties()
            .Select(p => (Prop: p, Attr: p.GetCustomAttribute<CborPropertyAttribute>()))
            .Where(x => x.Attr?.Index != null)
            .ToDictionary(x => x.Attr!.Index!.Value, x => x.Prop.PropertyType);
}