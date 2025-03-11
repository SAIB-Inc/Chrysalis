using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

[CborConverter(typeof(UnionConverter))]
public abstract partial record TransactionOutput : CborBase;


[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record AlonzoTransactionOutput(
    [CborIndex(0)] Address Address,
    [CborIndex(1)] Value Amount,
    [CborIndex(2)] CborBytes? DatumHash
) : TransactionOutput;


[CborConverter(typeof(CustomMapConverter))]
[CborOptions(IsDefinite = true)]
public partial record PostAlonzoTransactionOutput(
    [CborIndex(0)] Address Address,
    [CborIndex(1)] Value Amount,
    [CborIndex(2)] DatumOption? Datum,
    [CborIndex(3)] CborEncodedValue? ScriptRef
) : TransactionOutput;