using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Cbor;

[CborSerializable(CborType.List, IsDefinite = true)]
public record CborDefiniteList<T>(T[] Value) : ICbor where T : ICbor;

[CborSerializable(CborType.List, IsDefinite = false)]
public record CborIndefiniteList<T>(T[] Value) : ICbor where T : ICbor;