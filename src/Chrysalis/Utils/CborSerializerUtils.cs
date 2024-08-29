using System.Formats.Cbor;

namespace Chrysalis.Utils;

public class CborSerializerUtils
{
    private const int BaseTagValue = 121;

    public static CborTag GetCborTag(int? index = null)
    {
        int actualIndex = index ?? 0;
        return (CborTag)(BaseTagValue + actualIndex);
    }
}