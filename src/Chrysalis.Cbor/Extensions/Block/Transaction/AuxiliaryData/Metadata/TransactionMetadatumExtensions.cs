using Chrysalis.Cbor.Cardano.Types.Block.Transaction;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.AuxiliaryData.Metadata;

public static class TransactionMetadatumExtensions
{
    public static byte[] Raw(this TransactionMetadatum self) => self.Raw?.ToArray() ?? [];
}