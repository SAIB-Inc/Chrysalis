using System.Reflection;
using ChrysalisV2.Attributes;
using ChrysalisV2.Types.Core;

namespace ChrysalisV2.Extensions.Core;

public static class ICborExtension
{
    public static byte[] Serialize(this ICbor self)
    {
        // Get the type of the object
        Type type = self.GetType();

        // Check if the type has the CborSerializableAttribute
        CborSerializableAttribute attribute = type.GetCustomAttribute<CborSerializableAttribute>()
            ?? throw new Exception($"The type {type.Name} is not marked with CborSerializableAttribute.");

        // Resolve the base type from the attribute
        Type baseType = attribute.CborType
            ?? throw new Exception($"The CborSerializableAttribute on {type.Name} does not specify a valid base type.");

        // Ensure that the base type is compatible and has a Serialize method
        MethodInfo? serializeMethod = type.Assembly.GetTypes()
            .Where(t => t.IsClass && t.IsAbstract && t.IsSealed)  // Static classes are both abstract and sealed
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .FirstOrDefault(m =>
                m.Name == "Serialize" &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == baseType)
                    ?? throw new Exception($"No Serialize extension method found for type {baseType.Name}.");  // Match the parameter type

        // Now, call the Serialize method of the base type
        object? result = serializeMethod.Invoke(null, [self]);

        // Ensure the result is a byte array
        return result as byte[] ?? throw new Exception("Failed to serialize object to a byte array.");
    }

    public static T Deserialize<T>(this byte[] self) where T : ICbor
    {
        throw new NotImplementedException();
    }
}