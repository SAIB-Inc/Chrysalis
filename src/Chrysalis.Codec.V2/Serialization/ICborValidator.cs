namespace Chrysalis.Codec.V2.Serialization;

public interface ICborValidator<T>
{
    bool Validate(T input);
}
