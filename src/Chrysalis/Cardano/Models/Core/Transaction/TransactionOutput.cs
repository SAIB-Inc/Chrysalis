using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Plutus;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(BabbageTransactionOutput),
    typeof(AlonzoTransactionOutput),
    typeof(MaryTransactionOutput),
    typeof(ShellyTransactionOutput)
])]
public record TransactionOutput : ICbor;

[CborSerializable(CborType.List)]
public record ShellyTransactionOutput(
    [CborProperty(0)] Address Address,
    [CborProperty(1)] Lovelace Amount
) : TransactionOutput;

[CborSerializable(CborType.List)]
public record MaryTransactionOutput(
    [CborProperty(0)] Address Address,
    [CborProperty(1)] Value Amount
) : TransactionOutput;

[CborSerializable(CborType.List)]
public record AlonzoTransactionOutput(
    [CborProperty(0)] Address Address,
    [CborProperty(1)] Value Amount,
    [CborProperty(2)] CborBytes DatumHash
) : TransactionOutput;

[CborSerializable(CborType.Map)]
public record BabbageTransactionOutput(
    [CborProperty(0)] Address Address,
    [CborProperty(1)] Value Amount,
    [CborProperty(2)] DatumOption? Datum,
    [CborProperty(3)] CborEncodedValue? ScriptRef
) : TransactionOutput;