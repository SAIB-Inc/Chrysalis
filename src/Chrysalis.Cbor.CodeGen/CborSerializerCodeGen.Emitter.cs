using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private interface ICborSerializerEmitter
    {
        string EmitCborSerializer(StringBuilder sb, SerializableTypeMetadata serializerContext);
        string EmitCborDeserializer(StringBuilder sb, SerializableTypeMetadata serializerContext);
    }

    private static class Emitter
    {
        private static StringBuilder EmitGenericCborReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            switch (metadata.SerializationType)
            {
                case SerializationType.Container:

                    break;
                case SerializationType.Map:
                    break;
                case SerializationType.List:
                    break;
                case SerializationType.Constr:
                    break;
                case SerializationType.Union:
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"Unknown serialization type: {metadata.SerializationType}");
            }
            return sb;
        }

        private static StringBuilder EmitGenericCborReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName)
        {
            if (metadata.IsNullable)
            {
                sb.AppendLine($"if (reader.PeekState() == CborReaderState.Null)");
                sb.AppendLine("{");
                sb.AppendLine($"{propertyName} = null;");
                sb.AppendLine("}");
                sb.AppendLine($"else");
                sb.AppendLine("{");
            }

            if (IsPrimitiveType(metadata.PropertyType))
            {
                EmitPrimitiveCborReader(sb, metadata.PropertyType, propertyName);
            }
            else
            {
                EmitCustomCborReader(sb, metadata, propertyName);
            }

            if (metadata.IsNullable)
            {
                sb.AppendLine("}");
            }

            return sb;
        }

        private static StringBuilder EmitPrimitiveCborReader(StringBuilder sb, string type, string propertyName)
        {
            switch (type)
            {
                case "bool":
                    sb.AppendLine($"{propertyName} = reader.ReadBoolean();");
                    break;
                case "int":
                    sb.AppendLine($"{propertyName} = reader.ReadInt32();");
                    break;
                case "long":
                    sb.AppendLine($"{propertyName} = reader.ReadInt64();");
                    break;
                case "ulong":
                    sb.AppendLine($"{propertyName} = reader.ReadUInt64();");
                    break;
                case "string":
                    sb.AppendLine($"{propertyName} = reader.ReadString();");
                    break;
                case "byte[]":
                    sb.AppendLine($"{propertyName} = reader.PeekState() switch");
                    sb.AppendLine("{");
                    sb.AppendLine("    CborReaderState.StartIndefiniteLengthByteString =>");
                    sb.AppendLine("    {");
                    sb.AppendLine("        using (var stream = new MemoryStream())");
                    sb.AppendLine("        {");
                    sb.AppendLine("            reader.ReadStartIndefiniteLengthByteString();");
                    sb.AppendLine("            while (reader.PeekState() != CborReaderState.EndIndefiniteLengthByteString)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                byte[] chunk = reader.ReadByteString();");
                    sb.AppendLine("                stream.Write(chunk, 0, chunk.Length);");
                    sb.AppendLine("            }");
                    sb.AppendLine("            reader.ReadEndIndefiniteLengthByteString();");
                    sb.AppendLine("            return stream.ToArray();");
                    sb.AppendLine("        }");
                    sb.AppendLine("    }");
                    sb.AppendLine("    _ =>");
                    sb.AppendLine("    {");
                    sb.AppendLine("        return reader.ReadByteString();");
                    sb.AppendLine("    }");
                    sb.AppendLine("};");
                    break;
                case "CborEncodedValue":
                    sb.AppendLine($"{propertyName} = reader.ReadEncodedValue();");
                    break;
            }

            return sb;
        }

        private static bool IsPrimitiveType(string type) => type switch
        {
            "bool" => true,
            "int" => true,
            "long" => true,
            "ulong" => true,
            "string" => true,
            "byte[]" => true,
            "CborEncodedValue" => true,
            _ => false
        };

        private static StringBuilder EmitCustomCborReader(StringBuilder sb, SerializablePropertyMetadata metadata, string propertyName)
        {
            if (metadata.IsList)
            {
                if (metadata.ListItemTypeFullName is null || metadata.ListItemType is null)
                {
                    throw new InvalidOperationException($"List item type is null for property {metadata.PropertyName}");
                }

                Parser._cache.TryGetValue(metadata.ListItemTypeFullName, out SerializableTypeMetadata? listItemMetadata);

                if (listItemMetadata is null)
                {
                    throw new InvalidOperationException($"List item metadata is null for property {metadata.PropertyName}");
                }

                sb.AppendLine($"{metadata.ListItemTypeFullName} {propertyName}TempItem = default;");
                sb.AppendLine($"List<{metadata.ListItemTypeFullName}> {propertyName}TempList = new();");
                sb.AppendLine($"reader.ReadStartArray();");
                sb.AppendLine($"while (reader.PeekState() != CborReaderState.EndArray)");
                sb.AppendLine("{");

                if (IsPrimitiveType(metadata.ListItemType))
                {
                    EmitPrimitiveCborReader(sb, metadata.ListItemType, $"{propertyName}TempItem");
                    sb.AppendLine($"{propertyName}TempList.Add({propertyName}TempItem);");
                }
                else
                {
                    sb.AppendLine($"{propertyName}TempItem = {metadata.ListItemTypeFullName}.Read(reader.ReadEncodedValue());");
                    sb.AppendLine($"{propertyName}TempList.Add({propertyName}TempItem);");
                }

                sb.AppendLine("}");
                sb.AppendLine($"reader.ReadEndArray();");
                sb.AppendLine($"{propertyName} = {propertyName}TempList;");
            }

            if (metadata.IsMap)
            {
                if (metadata.MapKeyTypeFullName is null ||
                    metadata.MapValueTypeFullName is null ||
                    metadata.MapKeyType is null ||
                    metadata.MapValueType is null
                )
                {
                    throw new InvalidOperationException($"Map key or value type is null for property {metadata.PropertyName}");
                }

                Parser._cache.TryGetValue(metadata.MapKeyTypeFullName, out SerializableTypeMetadata? mapKeyMetadata);
                Parser._cache.TryGetValue(metadata.MapValueTypeFullName, out SerializableTypeMetadata? mapValueMetadata);

                if (mapKeyMetadata is null || mapValueMetadata is null)
                {
                    throw new InvalidOperationException($"Map key or value metadata is null for property {metadata.PropertyName}");
                }

                sb.AppendLine($"Dictionary<{metadata.MapKeyTypeFullName}, {metadata.MapValueTypeFullName}> {propertyName}TempMap = new();");
                sb.AppendLine($"{metadata.MapKeyTypeFullName} {propertyName}TempKeyItem = default;");
                sb.AppendLine($"{metadata.MapValueTypeFullName} {propertyName}TempValueItem = default;");
                sb.AppendLine($"reader.ReadStartMap();");
                sb.AppendLine($"while (reader.PeekState() != CborReaderState.EndMap)");
                sb.AppendLine("{");

                if (IsPrimitiveType(metadata.MapKeyType))
                {
                    EmitPrimitiveCborReader(sb, metadata.MapKeyType, $"{propertyName}TempKeyItem");
                }
                else
                {
                    sb.AppendLine($"{propertyName}TempKeyItem = {metadata.MapKeyTypeFullName}.Read(reader.ReadEncodedValue());");
                }

                if (IsPrimitiveType(metadata.MapValueType))
                {
                    EmitPrimitiveCborReader(sb, metadata.MapValueType, $"{propertyName}TempValueItem");
                }
                else
                {
                    sb.AppendLine($"{propertyName}TempValueItem = {metadata.MapValueTypeFullName}.Read(reader.ReadEncodedValue());");
                }

                sb.AppendLine($"{propertyName}TempMap.Add({propertyName}TempKeyItem, {propertyName}TempValueItem);");

                sb.AppendLine("}");
                sb.AppendLine($"reader.ReadEndMap();");
                sb.AppendLine($"{propertyName} = {propertyName}TempMap;");
            }

            // otherwise, handle as custom type
            sb.AppendLine($"{propertyName}Raw = reader.ReadEncodedValue();");
            sb.AppendLine($"{propertyName} = {metadata.PropertyTypeFullName}.Read({propertyName}Raw)");

            return sb;
        }
    }


}
