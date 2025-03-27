using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models;
using Chrysalis.Wallet.Models.Addresses;
using CborAddress = Chrysalis.Cbor.Types.Cardano.Core.Common.Address;

namespace Chrysalis.Tx.Extensions;

public static class OutputOptionsExtension
{
    public static TransactionOutput BuildOutput(this OutputOptions options, Dictionary<string, string> parties)
    {
        Address address = Address.FromBech32(parties[options.To!]);
        if (options.Datum != null)
        {
            return new PostAlonzoTransactionOutput(new CborAddress(address.ToBytes()), options.Amount!, options.Datum, null);
        }
        return new AlonzoTransactionOutput(new CborAddress(address.ToBytes()), options.Amount!, null);
    }

}