using System.Runtime.CompilerServices;

namespace Chrysalis.Cbor.Extensions;

public static class TypeExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Type NormalizeType(this Type type) =>
        type.IsGenericType ? type.GetGenericTypeDefinition() : type;
}