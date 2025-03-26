using CMetadata = Chrysalis.Cbor.Types.Cardano.Core.Metadata;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.AuxiliaryData.Metadata;

public static class MetadataExtensions
{
    public static Dictionary<ulong, TransactionMetadatum> Value(this CMetadata self) => self.Value;
}