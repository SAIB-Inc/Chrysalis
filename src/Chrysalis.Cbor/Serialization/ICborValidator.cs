namespace Chrysalis.Cbor.Serialization;

public interface ICborValidator<T>
{
    bool Validate(T input);
}