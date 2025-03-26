using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.AuxiliaryData.Metadata;

public static class TransactionMetadatumExtensions
{
    public static byte[] Raw(this TransactionMetadatum self) => self.Raw?.ToArray() ?? [];
}