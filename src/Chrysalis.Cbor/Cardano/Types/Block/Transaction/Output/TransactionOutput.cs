using Chrysalis.Cbor.Attributes;
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
        [CborIndex(0)] Address Address,
        [CborIndex(1)] Value Amount,
        [CborIndex(2)] byte[]? DatumHash
    ) : TransactionOutput;


    [CborSerializable]
    [CborMap]
    public partial record PostAlonzoTransactionOutput(
        [CborIndex(0)] Address Address,
        [CborIndex(1)] Value Amount,
        [CborIndex(2)] DatumOption? Datum,
        [CborIndex(3)][CborSize(32)] byte[]? ScriptRef
    ) : TransactionOutput;
}
