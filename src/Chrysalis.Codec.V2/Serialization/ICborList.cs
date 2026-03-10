namespace Chrysalis.Codec.V2.Serialization;

public interface ICborList<T> : ICborType
{
    CborListEnumerator<T> GetEnumerator();
}
