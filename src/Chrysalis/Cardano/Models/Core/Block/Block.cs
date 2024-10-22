using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Block;
using Chrysalis.Cardano.Models.Core.Block.Transaction;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.List)]
public record BlockEntity(
    [CborProperty(0)] BlockHeader Header,
    [CborProperty(1)] CborMaybeIndefList<TransactionBody> TransactionBodies,
    [CborProperty(2)] CborMaybeIndefList<TransactionWitnessSet> TransactionWitnessSets,
    [CborProperty(3)] AuxiliaryDataSet AuxiliaryDataSet,
    [CborProperty(4)] CborMaybeIndefList<CborInt> InvalidTransactions
) : RawCbor;