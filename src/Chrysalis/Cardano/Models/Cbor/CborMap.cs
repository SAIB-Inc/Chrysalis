using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Map)]
public record CborMap(Dictionary<ICbor, ICbor> Value) : RawCbor;

[CborSerializable(CborType.Map)]
public record CborMap<T, V>(Dictionary<T, V> Value) : RawCbor where T : ICbor where V : ICbor;