using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Tx.Models;
using TxAddr = Chrysalis.Tx.Models.Addresses;

namespace Chrysalis.Tx.Utils;

public class OutputUtils
{
    public static TransactionOutput BuildOutput(OutputOptions options, Dictionary<string, string> parties)
    {
        TxAddr.Address address = TxAddr.Address.FromBech32(parties[options.To!]);
        if(options.Datum != null)
        {
            return new PostAlonzoTransactionOutput(new Address(address.ToBytes()), options.Amount!, options.Datum, null);
        }
        return new AlonzoTransactionOutput(new Address(address.ToBytes()), options.Amount!, null);
    }

}