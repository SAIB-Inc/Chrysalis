using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace CHrysalis.Cbor.Extensions;

public static class CborEncodedValueExtensions
{
    public static byte[] GetValue(this CborEncodedValue self)
    {
        CborReader reader = new(self.Value, CborConformanceMode.Lax);
        reader.ReadTag();
        return reader.ReadByteString();
    }
}