using Chrysalis.Cardano.Core.Types.Block.Header;
using Chrysalis.Cardano.Core.Types.Block.Transaction;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block;

[CborConverter(typeof(UnionConverter))]
public abstract record Block : CborBase;

[CborConverter(typeof(CustomListConverter))]
public record ShelleyBlock(
    [CborProperty(0)] BlockHeader Header,
    [CborProperty(1)] CborMaybeIndefList<TransactionBody> TransactionBodies,
    [CborProperty(2)] CborMaybeIndefList<TransactionWitnessSet> TransactionWitnessSets,
    [CborProperty(3)] AuxiliaryDataSet TransactionMetadataSet
) : Block;

[CborConverter(typeof(CustomListConverter))]
public record AllegraBlock(
    [CborProperty(0)] BlockHeader Header,
    [CborProperty(1)] CborMaybeIndefList<TransactionBody> TransactionBodies,
    [CborProperty(2)] CborMaybeIndefList<TransactionWitnessSet> TransactionWitnessSets,
    [CborProperty(3)] AuxiliaryDataSet AuxiliaryDataSet
) : Block;

[CborConverter(typeof(CustomListConverter))]
public record PostMaryBlock(
    [CborProperty(0)] BlockHeader Header,
    [CborProperty(1)] CborMaybeIndefList<TransactionBody> TransactionBodies,
    [CborProperty(2)] CborMaybeIndefList<TransactionWitnessSet> TransactionWitnessSets,
    [CborProperty(3)] AuxiliaryDataSet AuxiliaryDataSet,
    [CborProperty(4)] CborMaybeIndefList<CborInt>? InvalidTransactions
) : Block;


