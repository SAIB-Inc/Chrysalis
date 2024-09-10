using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(BabbageBlock),
    typeof(AlonzoBlock),
    typeof(ConwayBlock),
])]
public record Block : ICbor;

[CborSerializable(CborType.List)]
public record ConwayBlock(
    [CborProperty(0)] BlockHeader Header,
    [CborProperty(1)] CborIndefiniteList<TransactionBody> TransactionBodies,
    [CborProperty(2)] CborIndefiniteList<TransactionWitnessSet> TransactionWitnessSets, //@TODO: Finish TransactionWitnessSets
    [CborProperty(3)] CborMap<CborBytes,CborBytes> AuxiliaryDataSet, //@TODO: Define auxiliary_data_set
    [CborProperty(4)] CborIndefiniteList<CborInt> InvalidTransactions
) : Block;

[CborSerializable(CborType.List)]
public record BabbageBlock(
    [CborProperty(0)] BlockHeader Header,
    [CborProperty(1)] CborIndefiniteList<TransactionBody> TransactionBodies
) : Block;

[CborSerializable(CborType.List)]
public record AlonzoBlock(
    [CborProperty(0)] BlockHeader Header,
    [CborProperty(1)] CborIndefiniteList<TransactionBody> TransactionBodies
) : Block;