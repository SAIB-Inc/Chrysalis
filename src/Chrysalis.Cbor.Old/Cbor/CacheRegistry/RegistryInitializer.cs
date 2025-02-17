
namespace Chrysalis.Cbor.CacheRegistry;

internal sealed class RegistryInitializer
{
    private int _isInitialized;
    private readonly Lock _lockObject = new();
    private readonly Registry _registry;

    internal RegistryInitializer(Registry registry)
    {
        _registry = registry;
    }

    internal void EnsureInitialized()
    {
        if (Interlocked.CompareExchange(ref _isInitialized, 1, 0) == 0)
        {
            lock (_lockObject)
            {
                try
                {
                    _registry.Initialize();
                }
                catch
                {
                    Interlocked.Exchange(ref _isInitialized, 0);
                    throw;
                }
            }
        }
    }
}