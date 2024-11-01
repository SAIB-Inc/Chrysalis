using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Cbor;

[CborSerializable(CborType.Ulong)]
public record CborUlong(ulong Value) : RawCbor;

public record PosixTime(ulong Value) : CborUlong(Value);