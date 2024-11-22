using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record BlockWithEra(
    [CborProperty(0)] CborInt EraNumber,
    [CborProperty(1)] Block Block
) : RawCbor;