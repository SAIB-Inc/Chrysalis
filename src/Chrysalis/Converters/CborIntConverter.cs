using System.Formats.Cbor;
using Chrysalis.Types.Core;

namespace Chrysalis.Converters;

public class CborIntConverter : ICborConverter<CborInt>
{
    public CborInt Deserialize(ReadOnlyMemory<byte> data)
    {
        throw new NotImplementedException();
    }

    public ReadOnlyMemory<byte> Serialize(CborInt data)
    {
        CborWriter writer = new();
        writer.WriteInt32(data.Value);
        return writer.Encode();
    }
}