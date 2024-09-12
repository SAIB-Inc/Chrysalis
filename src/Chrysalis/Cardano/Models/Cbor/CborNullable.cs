using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Nullable)]
public record CborNullable<T>(T Value) : ICbor where T : ICbor;