using System.Formats.Cbor;
using ChrysalisV2.Types.Core;

namespace ChrysalisV2.Extensions.Core;

public static class CborBoolExtension
{
    public static byte[] Serialize(this CborBool self)
    {
        CborWriter writer = new();
        writer.WriteBoolean(self.Value);
        return writer.Encode();
    }

    public static CborBool Deserialize(this byte[] self)
    {
        CborReader reader = new(self);
        return new CborBool(reader.ReadBoolean());
    }
}