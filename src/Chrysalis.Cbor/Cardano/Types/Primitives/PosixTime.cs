using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Primitives;

public partial record PosixTime(ulong Value) : CborUlong(Value);
