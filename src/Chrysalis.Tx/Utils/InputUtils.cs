using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Tx.Utils;

public class InputUtils
{
    public static TransactionInput BuildInput(string txHash,  ulong index)
    {
        return new TransactionInput(new CborBytes(Convert.FromHexString(txHash)), new CborUlong(index));
    }

}