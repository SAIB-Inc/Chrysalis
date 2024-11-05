using Chrysalis.Cbor;
using Chrysalis.Cardano.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record Block(
    [CborProperty(0)] BlockHeader Header,
    [CborProperty(1)] CborMaybeIndefList<TransactionBody> TransactionBodies,
    [CborProperty(2)] CborMaybeIndefList<TransactionWitnessSet> TransactionWitnessSets,
    [CborProperty(3)] AuxiliaryDataSet AuxiliaryDataSet,
    [CborProperty(4)] CborMaybeIndefList<CborInt> InvalidTransactions
) : RawCbor;