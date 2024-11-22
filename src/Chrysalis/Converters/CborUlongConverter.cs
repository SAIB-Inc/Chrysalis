using System.Formats.Cbor;
using Chrysalis.Types.Core;

namespace Chrysalis.Converters;

public class CborUlongConverter : ICborConverter<CborUlong>
{
    public CborUlong Deserialize(ReadOnlyMemory<byte> data)
    {
        throw new NotImplementedException();
    }

    public ReadOnlyMemory<byte> Serialize(CborUlong data)
    {
        CborWriter writer = new();
        writer.WriteUInt64(data.Value);
        return writer.Encode();
    }
}