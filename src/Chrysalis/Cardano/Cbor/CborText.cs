using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Cbor;

[CborSerializable(CborType.Text)]
public record CborText(string Value) : RawCbor;