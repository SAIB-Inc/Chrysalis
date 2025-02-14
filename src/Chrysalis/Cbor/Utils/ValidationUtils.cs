using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Utils.Exceptions;

namespace Chrysalis.Cbor.Utils;

internal static class ValidationUtils
{
    public static void ValidateType(Type type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        if (!TypeUtils.IsCborType(type) && !typeof(ICborConverter).IsAssignableFrom(type))
            throw new RegistryException($"Type {type.Name} must inherit from CborBase or implement ICborConverter");
    }
}