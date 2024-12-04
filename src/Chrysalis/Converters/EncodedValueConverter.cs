using System.Formats.Cbor;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class EncodedValueConverter : ICborConverter<CborEncodedValue>
{
    public byte[] Serialize(CborEncodedValue value)
    {
        CborWriter writer = new();

        // Write tag for encoded CBOR data item
        writer.WriteTag(CborTag.EncodedCborDataItem);

        // Write the encoded bytes as a byte string
        writer.WriteByteString(value.Value);

        return [.. writer.Encode()];
    }

    public ICbor Deserialize(byte[] data, Type? targetType = null)
    {
        CborReader reader = new CborReader(data);

        // Read and verify tag
        CborTag tag = reader.ReadTag();
        if (tag != CborTag.EncodedCborDataItem)
            throw new InvalidOperationException($"Expected EncodedCborDataItem tag, got {tag}");

        // Read the byte string
        return new CborEncodedValue(reader.ReadByteString());
    }
}