using CTransaction = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Transaction;
using CAuxiliaryData = Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet.AuxiliaryData;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.Transaction;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;

namespace Chrysalis.Cbor.Extensions.Block.Transaction;

public static class TransactionExtensions
{
    public static TransactionBody TransactionBody(this CTransaction self) => 
        self switch
        {
            ShelleyTransaction shelleyTx => shelleyTx.TransactionBody,
            AllegraTransaction allegraTx => allegraTx.TransactionBody,
            PostMaryTransaction postMaryTx => postMaryTx.TransactionBody,
            _ => throw new NotImplementedException()
        };

    public static TransactionWitnessSet WitnessSet(this CTransaction self) =>
        self switch
        {
            ShelleyTransaction shelleyTx => shelleyTx.TransactionWitnessSet,
            AllegraTransaction allegraTx => allegraTx.TransactionWitnessSet,
            PostMaryTransaction postMaryTx => postMaryTx.TransactionWitnessSet,
            _ => throw new NotImplementedException()
        };

    public static CAuxiliaryData? AuxiliaryData(this CTransaction self) =>
        self switch
        {
            AllegraTransaction allegraTx => allegraTx.AuxiliaryData,
            PostMaryTransaction postMaryTx => postMaryTx.AuxiliaryData,
            _ => null
        };
}