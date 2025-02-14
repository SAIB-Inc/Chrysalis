using System.Formats.Cbor;

namespace Chrysalis.Cbor.Utils;

public static class CborReaderFactory
{
    public static CborReader Create(byte[] data)
    {
        return new CborReader(data, CborConformanceMode.Lax);
    }

    public static CborReader Create(ReadOnlyMemory<byte> data)
    {
        return new CborReader(data, CborConformanceMode.Lax);
    }
}