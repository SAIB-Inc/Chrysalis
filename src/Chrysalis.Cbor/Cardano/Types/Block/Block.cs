using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Cardano.Types.Block.Header;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block;

[CborConverter(typeof(UnionConverter))]
public abstract record Block : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record AlonzoCompatibleBlock(
    [CborIndex(0)] BlockHeader Header,
    [CborIndex(1)] CborMaybeIndefList<AlonzoTransactionBody> TransactionBodies,
    [CborIndex(2)] CborMaybeIndefList<AlonzoTransactionWitnessSet> TransactionWitnessSets,
    [CborIndex(3)] AuxiliaryDataSet AuxiliaryDataSet,
    [CborIndex(4)] CborMaybeIndefList<CborInt>? InvalidTransactions
) : Block;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record BabbageBlock(
    [CborIndex(0)] BlockHeader Header,
    [CborIndex(1)] CborMaybeIndefList<BabbageTransactionBody> TransactionBodies,
    [CborIndex(2)] CborMaybeIndefList<PostAlonzoTransactionWitnessSet> TransactionWitnessSets,
    [CborIndex(3)] AuxiliaryDataSet AuxiliaryDataSet,
    [CborIndex(4)] CborMaybeIndefList<CborInt>? InvalidTransactions
) : Block;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record ConwayBlock(
    [CborIndex(0)] BlockHeader Header,
    [CborIndex(1)] CborMaybeIndefList<ConwayTransactionBody> TransactionBodies,
    [CborIndex(2)] CborMaybeIndefList<PostAlonzoTransactionWitnessSet> TransactionWitnessSets,
    [CborIndex(3)] AuxiliaryDataSet AuxiliaryDataSet,
    [CborIndex(4)] CborMaybeIndefList<CborInt>? InvalidTransactions
) : Block;
