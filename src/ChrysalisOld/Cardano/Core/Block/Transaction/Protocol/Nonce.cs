using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record Nonce(
    [CborProperty(0)] CborUlong Variant,
    [CborProperty(1)] CborBytes? Hash
) : RawCbor;