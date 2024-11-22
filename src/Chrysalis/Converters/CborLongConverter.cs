using System.Formats.Cbor;
using Chrysalis.Types.Core;

namespace Chrysalis.Converters;

public class CborLongConverter : ICborConverter<CborLong>
{
    public CborLong Deserialize(ReadOnlyMemory<byte> data)
    {
        throw new NotImplementedException();
    }

    public ReadOnlyMemory<byte> Serialize(CborLong data)
    {
        CborWriter writer = new();
        writer.WriteInt64(data.Value);
        return writer.Encode();
    }
}