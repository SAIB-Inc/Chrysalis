using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;

namespace Chrysalis.Tx.Utils;

public class InputUtils
{
    public static TransactionInput BuildInput(string txHash,  ulong index)
    {
        return new TransactionInput(Convert.FromHexString(txHash), index);
    }

}