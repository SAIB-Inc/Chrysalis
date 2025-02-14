using Chrysalis.Cbor.Utils.Exceptions;

namespace Chrysalis.Cbor.Utils.Validation;

internal static class Guard
{
    internal static void NotNull<T>(T value, string paramName) where T : class
    {
        if (value is null)
            throw new ArgumentNullException(paramName);
    }

    internal static void TypeMustBeCbor(Type type)
    {
        if (!TypeScanner.IsCborType(type))
            throw new RegistryException($"Type {type.Name} must inherit from CborBase");
    }
}