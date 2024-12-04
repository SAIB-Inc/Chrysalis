using System.Formats.Cbor;
using Chrysalis.Types;

namespace Chrysalis.Converters;

public class MapConverter<TKey, TValue> : ICborConverter<CborMap<TKey, TValue>>
    where TKey : ICbor
    where TValue : ICbor
{
    public byte[] Serialize(CborMap<TKey, TValue> value)
    {
        CborWriter writer = new();
        writer.WriteStartMap(value.Value.Count);

        foreach (KeyValuePair<TKey, TValue> kvp in value.Value)
        {
            writer.WriteEncodedValue(CborSerializer.Serialize(kvp.Key));
            writer.WriteEncodedValue(CborSerializer.Serialize(kvp.Value));
        }

        writer.WriteEndMap();
        return writer.Encode();
    }

    public ICbor Deserialize(byte[] data, Type? targetType = null)
    {
        CborReader reader = new(data);
        reader.ReadStartMap();

        Dictionary<TKey, TValue> map = [];

        while (reader.PeekState() != CborReaderState.EndMap)
        {
            TKey key = CborSerializer.Deserialize<TKey>(reader.ReadEncodedValue().ToArray());
            TValue value = CborSerializer.Deserialize<TValue>(reader.ReadEncodedValue().ToArray());
            map.Add(key, value);
        }

        reader.ReadEndMap();
        return new CborMap<TKey, TValue>(map);
    }
}
