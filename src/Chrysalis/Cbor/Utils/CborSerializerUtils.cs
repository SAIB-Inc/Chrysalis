using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor;

namespace Chrysalis.Cbor.Utils;

public static class CborSerializerUtils
{
    private const int BaseTagValue = 121;
    private const int ExtendedTagValue = 1280;

    public static CborTag GetCborTag(int? index = null)
    {
        int actualIndex = index ?? 0;
        int baseTagValue = actualIndex > 6 ? ExtendedTagValue - 7 : BaseTagValue;
        return (CborTag)(baseTagValue + actualIndex);
    }
    
    public static void ValidateTag(CborTag tag, Type targetType)
    {
        CborSerializableAttribute? cborSerializableAttribute = targetType.GetCustomAttribute<CborSerializableAttribute>();
        
        if (cborSerializableAttribute == null)
            throw new InvalidOperationException($"Target type {targetType.Name} does not have a CborSerializableAttribute.");

        int expectedTagValue = cborSerializableAttribute.Index >= 0 ? (int)GetCborTag(cborSerializableAttribute.Index) : (int)GetCborTag();
    
        if ((int)tag != expectedTagValue)
            throw new InvalidOperationException($"Invalid tag {tag} for {targetType.Name}. Expected tag: {expectedTagValue}");
    }

    public static object? GetValue(this ICbor cbor, Type objType)
    {
        if (cbor == null)
            throw new ArgumentNullException(nameof(cbor), "The CBOR object cannot be null.");

        if (objType == null)
            throw new ArgumentNullException(nameof(objType), "The target type cannot be null.");

        PropertyInfo? valueProperty = objType.GetProperty("Value") ?? 
            throw new InvalidOperationException($"Type {objType.Name} does not have a 'Value' property.");

        object? value = valueProperty.GetValue(cbor);

        return value;
    }

    public static CborType? GetCborType(this Type objType)
    {
        if (typeof(ICbor).IsAssignableFrom(objType))
        {
            CborSerializableAttribute? attr = objType.GetCustomAttribute<CborSerializableAttribute>();
            if (attr != null)
                return attr.Type;
        }
        return null;
    }

    public static IEnumerable<Type> GetBaseTypes(Type type)
    {
        var baseType = type.BaseType;
        while (baseType != null)
        {
            yield return baseType;
            baseType = baseType.BaseType;
        }
    }
}