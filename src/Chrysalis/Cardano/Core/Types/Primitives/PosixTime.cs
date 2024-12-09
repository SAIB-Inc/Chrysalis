using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Primitives;

public record PosixTime(ulong Value) : CborUlong(Value);
