using CPlutusData = Chrysalis.Cbor.Types.Cardano.Core.Common.PlutusData;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.Script.PlutusData;

public static class PlutusDataExtensions
{
    public static byte[] Raw(this CPlutusData self) => self.Raw?.ToArray() ?? [];
}