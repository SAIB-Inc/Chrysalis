using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Ulong)]
public record CborUlong(ulong Value) : ICbor;