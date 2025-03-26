using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Tx.Utils;

public class InputUtils
{
    public static TransactionInput BuildInput(string txHash,  ulong index)
    {
        return new TransactionInput(Convert.FromHexString(txHash), index);
    }

}