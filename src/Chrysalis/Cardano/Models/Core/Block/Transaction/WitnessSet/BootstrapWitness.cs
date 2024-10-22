using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block.Transaction.WitnessSet;

[CborSerializable(CborType.List)]
public record BootstrapWitness(
    [CborProperty(0)] CborBytes PublicKey,
    [CborProperty(1)] CborBytes Signature,
    [CborProperty(2)] CborBytes ChainCode,
    [CborProperty(3)] CborBytes Attributes
) : RawCbor;