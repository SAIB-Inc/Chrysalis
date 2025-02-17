using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Registry;

namespace Chrysalis.Cbor.Utils;

public static class ConstrSerializationUtil
{
    public static object? Read(CborReader reader, CborOptions options)
    {
        if (reader.PeekState() != CborReaderState.Tag)
            throw new InvalidOperationException("Constructor is expected to be tagged");

        int index = Math.Max(0, options.Index);
        CborTag expectedTag = CborUtils.ResolveTag(index);
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
}