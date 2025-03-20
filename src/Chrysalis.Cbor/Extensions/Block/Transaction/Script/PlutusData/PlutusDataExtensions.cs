using CPlutusData = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script.PlutusData;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Script.PlutusData;

public static class PlutusDataExtensions
{
    public static byte[] Raw(this CPlutusData self) => self.Raw?.ToArray() ?? [];
}