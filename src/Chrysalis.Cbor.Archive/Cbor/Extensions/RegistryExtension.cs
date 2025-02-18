namespace Chrysalis.Cbor.Extensions;

internal static class RegistryExtension
{

    public static bool HasOptions(this Registry registry, Type type)
        => registry.GetOptions(type) is not null;

}