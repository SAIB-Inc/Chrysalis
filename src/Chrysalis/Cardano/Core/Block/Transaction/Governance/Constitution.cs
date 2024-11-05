using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record Constitution(
    [CborProperty(0)] Anchor Anchor,
    [CborProperty(1)] CborNullable<CborBytes> ScriptHash
) : RawCbor;