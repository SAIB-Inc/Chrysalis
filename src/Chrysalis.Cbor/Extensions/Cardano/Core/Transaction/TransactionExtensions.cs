using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using CTransaction = Chrysalis.Cbor.Types.Cardano.Core.Transaction.Transaction;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;

public static class TransactionExtensions
{
    public static TransactionBody TransactionBody(this CTransaction self) =>
        self switch
        {
            ShelleyTransaction shelleyTx => shelleyTx.TransactionBody,
            AllegraTransaction allegraTx => allegraTx.TransactionBody,
            PostMaryTransaction postMaryTx => postMaryTx.TransactionBody,
            _ => throw new NotSupportedException()
        };
}