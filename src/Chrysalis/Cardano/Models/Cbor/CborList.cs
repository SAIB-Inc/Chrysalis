using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Cbor;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(CborDefiniteList<>), typeof(CborIndefiniteList<>)])]
public interface CborMaybeIndefList<T> : ICbor;

[CborSerializable(CborType.List, IsDefinite = true)]
public record CborDefiniteList<T>(T[] Value) : ICbor, CborMaybeIndefList<T> where T : ICbor;

[CborSerializable(CborType.List, IsDefinite = false)]
public record CborIndefiniteList<T>(T[] Value) : ICbor, CborMaybeIndefList<T> where T : ICbor;