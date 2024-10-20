using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block;

[CborSerializable(CborType.List)]
public record BlockWithEra(
    [CborProperty(0)] CborInt EraNumber,
    [CborProperty(1)] Block Block
) : RawCbor;