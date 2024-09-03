using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Constr, Index = 1)]
public record None<T> : Option<T>;