using Chrysalis.Cbor.Cardano.Types.Block.Transaction.WitnessSet;
using Chrysalis.Tx.Utils;

namespace Chrysalis.Tx.Extensions;

public static class CborExtensions
{
    public static byte[] AuxiliaryDataHash(this AuxiliaryData data) => HashUtil.Blake2b256(data.ToBytes() ?? []);
}