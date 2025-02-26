namespace Chrysalis.Cbor.Types;

public interface ICborNumber<T> {
    T Value { get; }
}