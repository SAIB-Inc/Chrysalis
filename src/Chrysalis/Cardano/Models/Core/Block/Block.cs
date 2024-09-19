using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Transaction;

namespace Chrysalis.Cardano.Models.Core.Block;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(AlonzoBlock),
])]
public record Block : ICbor;

[CborSerializable(CborType.List)]
public record AlonzoBlock(
    [CborProperty(0)] BlockHeader Header,
    [CborProperty(1)] CborMaybeIndefList<TransactionBody> TransactionBodies,
    [CborProperty(2)] CborMaybeIndefList<TransactionWitnessSet> TransactionWitnessSets,
    [CborProperty(3)] AuxiliaryDataSet AuxiliaryDataSet,
    [CborProperty(4)] CborMaybeIndefList<CborInt> InvalidTransactions
) : Block;

[CborSerializable(CborType.List)]
public record EbBlock(
    [CborProperty(0)] BlockHeader Header,
    [CborProperty(1)] CborIndefiniteList<CborBytes> Body,
    [CborProperty(2)] CborIndefiniteList<CborMap> Attributes //@TODO: To be modified
) : Block;



// Attributes
// quote from the CDDL file: at the moment we do not bother deserialising these,
// since they don't contain anything
// attributes = {* any => any}
// quote from Pallas Definition: pub type Attributes = EmptyMap; don't ask me why, that's what the CDDL asks for.