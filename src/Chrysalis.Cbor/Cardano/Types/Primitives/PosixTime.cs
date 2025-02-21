using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Primitives;

public record PosixTime(ulong Value) : CborUlong(Value);
