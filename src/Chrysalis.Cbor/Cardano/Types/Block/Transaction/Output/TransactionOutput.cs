using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

[CborSerializable]
[CborUnion]
public abstract partial record TransactionOutput : CborBase<TransactionOutput>
{
    [CborSerializable]
    [CborList]
    public partial record AlonzoTransactionOutput(
        [CborOrder(0)] Address Address,
        [CborOrder(1)] Value Amount,
        [CborOrder(2)] byte[]? DatumHash
    ) : TransactionOutput;

    [CborSerializable]
    [CborMap]
    public partial record PostAlonzoTransactionOutput(
        [CborProperty("0")] Address Address,
        [CborProperty("1")] Value Amount,
        [CborProperty("2")] DatumOption? Datum,
        [CborProperty("3")][CborSize(32)] byte[]? ScriptRef
    ) : TransactionOutput;
}
