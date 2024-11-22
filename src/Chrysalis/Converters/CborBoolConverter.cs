using System.Formats.Cbor;
using Chrysalis.Types.Core;

namespace Chrysalis.Converters;

public class CborBoolConverter : ICborConverter<CborBool>
{

    public ReadOnlyMemory<byte> Serialize(CborBool data)
    {
        CborWriter writer = new();
        writer.WriteBoolean(data.Value);
        return writer.Encode();
    }

    public CborBool Deserialize(ReadOnlyMemory<byte> data)
    {
        var reader = new CborReader(data);
        return new CborBool(reader.ReadBoolean());
    }
}