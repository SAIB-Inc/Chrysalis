using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;

namespace Chrysalis.Tx.Utils;


public static class Defaults
{
    public static ConwayTransactionBody TransactionBody => new(new CborDefListWithTag<TransactionInput>([]), new CborDefList<TransactionOutput>([]), 0, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
    public static PostAlonzoTransactionWitnessSet TransactionWitnessSet => new(null, null, null, null, null, null, null, null);

}