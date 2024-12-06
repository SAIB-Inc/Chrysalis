namespace Chrysalis.Cbor.Types.Collections;

public record CborIndefiniteList<T>(List<T> Value) : CborList<T>(Value) where T : CborBase;