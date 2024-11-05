using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record VKeyWitness(
    [CborProperty(0)] CborBytes VKey,
    [CborProperty(1)] CborBytes Signature
) : RawCbor;