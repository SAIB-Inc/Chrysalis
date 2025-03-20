using Chrysalis.Cbor.Cardano.Types.Block.Transaction;
using CMetadata = Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet.AuxiliaryData.Metadata;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.AuxiliaryData.Metadata;

public static class MetadataExtensions
{
    public static Dictionary<ulong, TransactionMetadatum> Value(this CMetadata self) => self.Value;
}