using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.List)]
public record VKeyWitness(
    [CborProperty(0)] CborBytes VKey,
    [CborProperty(1)] CborBytes Signature
) : RawCbor;