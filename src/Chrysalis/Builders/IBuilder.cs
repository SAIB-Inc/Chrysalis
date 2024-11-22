using Chrysalis.Cbor;

namespace Chrysalis.Builders;

public interface IBuilder<T> where T : ICbor
{
    T Build();
}