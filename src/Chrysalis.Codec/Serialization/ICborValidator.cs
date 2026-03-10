namespace Chrysalis.Codec.Serialization;

public interface ICborValidator<T>
{
    bool Validate(T input);
}
