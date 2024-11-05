using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Cbor;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(CborDefiniteList<>), 
    typeof(CborIndefiniteList<>),
    typeof(CborDefiniteListWithTag<>),
    typeof(CborIndefiniteListWithTag<>),
])]
public interface CborMaybeIndefList<T> : ICbor;

[CborSerializable(CborType.List, IsDefinite = true)]
public record CborDefiniteList<T>(T[] Value) : RawCbor, CborMaybeIndefList<T> where T : ICbor;

[CborSerializable(CborType.List, IsDefinite = false)]
public record CborIndefiniteList<T>(T[] Value) : RawCbor, CborMaybeIndefList<T> where T : ICbor;

[CborSerializable(CborType.Tag, Index = 258)]
public record CborDefiniteListWithTag<T>(CborDefiniteList<T> Value) : RawCbor, CborMaybeIndefList<T> where T : ICbor;

[CborSerializable(CborType.Tag, Index = 258)]
public record CborIndefiniteListWithTag<T>(CborIndefiniteList<T> Value) : RawCbor, CborMaybeIndefList<T> where T : ICbor;