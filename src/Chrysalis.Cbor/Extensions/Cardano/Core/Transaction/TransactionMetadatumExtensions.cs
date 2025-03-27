using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;

public static class TransactionMetadatumExtensions
{
    public static byte[] Raw(this TransactionMetadatum self) => self.Raw?.ToArray() ?? [];
}