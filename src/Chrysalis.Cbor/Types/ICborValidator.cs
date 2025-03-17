namespace Chrysalis.Cbor.Types;

public interface ICborValidator<T>
{
    bool Validate(T input);
}