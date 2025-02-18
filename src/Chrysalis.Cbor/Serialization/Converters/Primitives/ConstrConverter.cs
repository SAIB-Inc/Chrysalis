using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Serialization.Exceptions;
using Chrysalis.Cbor.Serialization.Registry;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class ConstrConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        if (reader.PeekState() != CborReaderState.Tag)
            throw new InvalidOperationException("Constructor is expected to be tagged");

        int index = Math.Max(0, options.Index);
        CborTag expectedTag = CborUtil.ResolveTag(index);
        CborTag tag = reader.ReadTag();

        if (tag != expectedTag)
            throw new InvalidOperationException($"Expected tag {expectedTag} but got {tag}");

        reader.ReadStartArray();

        List<object?> constructorArgs = [];
        ParameterInfo[]? parameters = options.Constructor?.GetParameters();
        if (parameters is not null && parameters.Length > 0)
        {
            bool isIndexBased = options.IndexPropertyMapping is not null && options.IndexPropertyMapping.Count > 0;
            if (isIndexBased)
            {
                object? result = CustomListSerializationUtil.Read(reader, options);
                if (result is not List<object?> resultList)
                    throw new InvalidOperationException("Expected List<object?> from CustomListSerializationUtil.Read");
                constructorArgs.AddRange(resultList);
            }
            else
            {
                for (int i = 0; i < parameters.Length && reader.PeekState() != CborReaderState.EndArray; i++)
                {
                    ParameterInfo parameter = parameters[i];
                    CborOptions innerOptions = CborRegistry.Instance.GetOptions(parameter.ParameterType);
                    object? item = CborSerializer.Deserialize(reader, innerOptions);
                    constructorArgs.Add(item);
                }
            }
        }

        reader.ReadEndArray();

        return constructorArgs;
    }

    public void Write(CborWriter writer, List<object?> value, CborOptions options)
    {
        if (options.Constructor is null)
            throw new CborSerializationException("Constructor cannot be null");

        int tag = Math.Max(0, options.Index);
        CborTag resolvedTag = CborUtil.ResolveTag(tag);
        writer.WriteTag(resolvedTag);

        writer.WriteStartArray(options.IsDefinite ? options.Size : null);
        CustomListSerializationUtil.Write(writer, value);
        writer.WriteEndArray();
    }
}