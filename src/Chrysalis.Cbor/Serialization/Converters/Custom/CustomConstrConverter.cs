using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Registry;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Serialization.Converters.Custom;

public sealed class CustomConstrConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        // In a custom constructor, we do not check for specific index
        // This converter is expected to only accept single list as constructor argument
        if (reader.PeekState() != CborReaderState.Tag)
            throw new InvalidOperationException("Custom constructor is expected to be tagged");

        reader.ReadTag();
        reader.ReadStartArray();

        if (options.RuntimeType is null)
            throw new InvalidOperationException("Runtime type is not defined in options.");

        Type innerType = options.RuntimeType.GetGenericArguments()[0];
        CborOptions innerOptions = CborRegistry.Instance.GetOptions(innerType);

        List<object?> items = [];
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            object? item = CborSerializer.Deserialize(reader, innerOptions);
            items.Add(item);
        }
        reader.ReadEndArray();

        return items;
    }

    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        // Write tag and start array 
        int tag = Math.Max(0, options.Index);
        CborTag resolvedTag = CborUtil.ResolveTag(tag);
        writer.WriteTag(resolvedTag);

        int count = value.Count(v => v is not null);
        writer.WriteStartArray(options.IsDefinite ? count : null);

        // Get inner type same way as read
        Type innerType = options.RuntimeType?.GetGenericArguments()[0]
            ?? throw new InvalidOperationException("Runtime type is not defined in options.");
        CborOptions innerOptions = CborRegistry.Instance.GetOptions(innerType);

        // Write each item
        foreach (object? item in value)
        {
            if (item is not null)
                CborSerializer.Serialize(writer, item, innerOptions);
        }

        writer.WriteEndArray();
    }
}