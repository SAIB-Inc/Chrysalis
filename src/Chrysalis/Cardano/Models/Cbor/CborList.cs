using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Cbor;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(CborDefiniteList<>), 
    typeof(CborIndefiniteList<>),
    typeof(CborDefiniteListWithTag<>),
    typeof(CborIndefiniteListWithTag<>),
])]
public interface CborMaybeIndefList<T> : ICbor;

[CborSerializable(CborType.List, IsDefinite = true)]
public record CborDefiniteList<T>(T[] Value) : ICbor, CborMaybeIndefList<T> where T : ICbor;

[CborSerializable(CborType.List, IsDefinite = false)]
public record CborIndefiniteList<T>(T[] Value) : ICbor, CborMaybeIndefList<T> where T : ICbor;

[CborSerializable(CborType.Tag, Index = 258)]
public record CborDefiniteListWithTag<T>(CborDefiniteList<T> Value) : ICbor, CborMaybeIndefList<T> where T : ICbor;

[CborSerializable(CborType.Tag, Index = 258)]
public record CborIndefiniteListWithTag<T>(CborIndefiniteList<T> Value) : ICbor, CborMaybeIndefList<T> where T : ICbor;