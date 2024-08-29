using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(Some<>), typeof(None<>)])]
public record Option<T> : ICbor;