
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Map)]
public record CborMap(Dictionary<ICbor, ICbor> Value) : ICbor;

[CborSerializable(CborType.Map)]
public record CborMap<T, V>(Dictionary<T, V> Value) : ICbor where T : ICbor where V : ICbor;