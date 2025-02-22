using System.Runtime.CompilerServices;

namespace Chrysalis.Cbor.Serialization.Registry;

public sealed class CborRegistry
{
    private static readonly CborRegistry Registry = new();
    public static CborRegistry Instance => Registry;

    private readonly CborConverterRegistry _converters = new();
    private readonly CborOptionsRegistry _options = new();


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ICborConverter GetConverter(Type type)
    {
        ICborConverter? converter = _converters.GetConverter(type)
            ?? throw new InvalidOperationException($"No converter found for type {type}");

        return converter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CborOptions GetBaseOptions(Type type)
    {
        CborOptions? options = _options.GetOptions(type)
            ?? throw new InvalidOperationException($"No options found for type {type}");

        return options with
        {
            RuntimeType = type
        };
    }
}