using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Output;

[CborConverter(typeof(UnionConverter))]
public abstract record TransactionOutput : CborBase;


[CborConverter(typeof(CustomListConverter))]
public record ShellyTransactionOutput(
    [CborProperty(0)] Address Address,
    [CborProperty(1)] Lovelace Amount
) : TransactionOutput;


[CborConverter(typeof(CustomListConverter))]
public record MaryTransactionOutput(
    [CborProperty(0)] Address Address,
    [CborProperty(1)] Value Amount
) : TransactionOutput;


[CborConverter(typeof(CustomListConverter))]
public record AlonzoTransactionOutput(
    [CborProperty(0)] Address Address,
    [CborProperty(1)] Value Amount,
    [CborProperty(2)] CborBytes DatumHash
) : TransactionOutput;


[CborConverter(typeof(CustomMapConverter))]
public record BabbageTransactionOutput(
    [CborProperty(0)] Address Address,
    [CborProperty(1)] Value Amount,
    [CborProperty(2)] DatumOption? Datum,
    [CborProperty(3)] CborEncodedValue? ScriptRef
) : TransactionOutput;