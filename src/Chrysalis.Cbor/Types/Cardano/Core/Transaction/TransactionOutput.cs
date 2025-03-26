using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborUnion]
public abstract partial record TransactionOutput : CborBase { }

[CborSerializable]
[CborList]
public partial record AlonzoTransactionOutput(
[CborOrder(0)] Address Address,
[CborOrder(1)] Value Amount,
[CborOrder(2)] byte[]? DatumHash
) : TransactionOutput, ICborPreserveRaw;

[CborSerializable]
[CborMap]
public partial record PostAlonzoTransactionOutput(
    [CborProperty(0)] Address Address,
    [CborProperty(1)] Value Amount,
    [CborProperty(2)] DatumOption? Datum,
    [CborProperty(3)] CborEncodedValue? ScriptRef
) : TransactionOutput, ICborPreserveRaw;
