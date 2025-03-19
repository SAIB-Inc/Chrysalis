using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Utils;

public class OutputUtils
{
    public static TransactionOutput BuildOutput(OutputOptions options, Dictionary<string, string> parties)
    {
        Address address = new(Convert.FromHexString(parties[options.To]));
        return new AlonzoTransactionOutput(address, options.Amount!, null);
    }

}