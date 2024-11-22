using Chrysalis.Cbor;
using Chrysalis.Cardano.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.List)]
public record Transaction : RawCbor
{
    [CborProperty(0)]
    public TransactionBody? TransactionBody { get; set; }

    [CborProperty(1)]
    public TransactionWitnessSet? TransactionWitnessSet { get; set; }

    [CborProperty(2)]
    public CborBool IsValid { get; set; } = new(false);

    [CborProperty(3)]
    public CborNullable<AuxiliaryData>? AuxiliaryData { get; set; }

    // parameterless constructor for builder
    public Transaction() { }

    public Transaction(TransactionBody transactionBody, TransactionWitnessSet transactionWitnessSet)
    {
        TransactionBody = transactionBody;
        TransactionWitnessSet = transactionWitnessSet;
    }

    public Transaction(TransactionBody? transactionBody, TransactionWitnessSet? transactionWitnessSet, CborBool? isValid, CborNullable<AuxiliaryData>? auxiliaryData)
    {
        TransactionBody = transactionBody;
        TransactionWitnessSet = transactionWitnessSet;
        AuxiliaryData = auxiliaryData;
        IsValid = isValid ?? new(false);
    }
}
