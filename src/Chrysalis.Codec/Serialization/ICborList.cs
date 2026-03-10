namespace Chrysalis.Codec.Serialization;

public interface ICborList<T> : ICborType
{
    CborListEnumerator<T> GetEnumerator();
}
