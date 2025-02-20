using System.Reflection;
using System.Runtime.CompilerServices;

namespace Chrysalis.Cbor.Serialization.Registry;

public sealed class CborRegistry
{
    private static readonly Lazy<CborRegistry> LazyInstance =
        new(() => new CborRegistry());
    public static CborRegistry Instance => LazyInstance.Value;

    private readonly CborConverterRegistry _converters = new();
    private readonly CborOptionsRegistry _options = new();
    private bool _isInitialized;


    public void Initialize()
    {
        if (_isInitialized)
            return;

        Assembly assembly = Assembly.GetExecutingAssembly();
        _converters.Initialize(assembly);
        _options.Initialize(assembly);

        _isInitialized = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ICborConverter GetConverter(Type type)
    {
        EnsureInitialized();
        ICborConverter? converter = _converters.GetConverter(type)
            ?? throw new InvalidOperationException($"No converter found for type {type}");

        return converter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CborOptions GetBaseOptions(Type type)
    {
        EnsureInitialized();
        CborOptions? options = _options.GetOptions(type)
            ?? throw new InvalidOperationException($"No options found for type {type}");

        return options with
        {
            RuntimeType = type
        };
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public CborOptions GetBaseOptionsWithContext(Type type, CborOptions contextOptions)
    {
        CborOptions options = GetBaseOptions(type);
        options.OriginalData = contextOptions.OriginalData;
        return options;
    }


    private void EnsureInitialized()
    {
        if (!_isInitialized)
            Initialize();
    }
}