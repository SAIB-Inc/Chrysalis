using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.Nullable)]
public record CborNullable<T>(T Value) : RawCbor where T : ICbor;