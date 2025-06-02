using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Tx.Utils;

// Rename
public static class CborTypeDefaults
{
    public static ConwayTransactionBody TransactionBody => new(
        new CborDefListWithTag<TransactionInput>([]),
        new CborDefList<TransactionOutput>([]), 0, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null
    );
    public static PostAlonzoTransactionWitnessSet TransactionWitnessSet => new(null, null, null, null, null, null, null, null);

    public static PostAlonzoAuxiliaryDataMap AuxiliaryData => new(
        null,
        null,
        null,
        null,
        null
    );

}