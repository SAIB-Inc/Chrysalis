using Chrysalis.Cbor;

namespace Chrysalis.Builders;

public abstract class BuilderBase<T> : IBuilder<T> where T : ICbor, new()
{
    private protected T Instance { get; }

    protected BuilderBase()
    {
        Instance = new T();
    }

    public abstract T Build();
}