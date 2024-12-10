using Chrysalis.Cardano.Core.Types.Block.Transaction.Body;
using Chrysalis.Cardano.Core.Types.Block.Transaction.WitnessSet;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction;

[CborConverter(typeof(CustomListConverter))]
public record Transaction(
    [CborProperty(0)] TransactionBody TransactionBody,
    [CborProperty(1)] TransactionWitnessSet TransactionWitnessSet,
    [CborProperty(2)] CborBool IsValid,
    [CborProperty(3)] CborNullable<AuxiliaryData> AuxiliaryData
) : CborBase;

