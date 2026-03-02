using System.Globalization;
using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private sealed class MapEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = Emitter.EmitCborReaderInstance(sb, "data");
            _ = Emitter.EmitTagReader(sb, metadata.CborTag, "tagIndex");
            bool isIntKey = metadata.Properties[0].PropertyKeyInt is not null;

            _ = sb.AppendLine($"Dictionary<{(isIntKey ? "int" : "string")}, object> resultMap = [];");

            // Read the map and size using Dahomey API
            _ = sb.AppendLine("reader.ReadBeginMap();");
            _ = sb.AppendLine("int mapSize = reader.ReadSize();");
            _ = sb.AppendLine("bool isIndefiniteMap = mapSize == -1;");
            _ = sb.AppendLine("int mapRemaining = mapSize;");

            _ = sb.AppendLine("while (isIndefiniteMap ? (reader.Buffer.Length > 0 && reader.Buffer[0] != 0xFF) : mapRemaining > 0)");
            _ = sb.AppendLine("{");

            _ = isIntKey
                ? sb.AppendLine($"int key = reader.ReadInt32();")
                : sb.AppendLine($"string key = reader.ReadString();");

            _ = sb.AppendLine($"object value = null;");

            // Per-key switch: dispatch each map value to the optimal reader for its known type
            _ = sb.AppendLine("switch (key)");
            _ = sb.AppendLine("{");
            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                string keyLiteral = isIntKey
                    ? prop.PropertyKeyInt?.ToString(CultureInfo.InvariantCulture)!
                    : $"\"{prop.PropertyKeyString}\"";
                _ = sb.AppendLine($"case {keyLiteral}:");
                _ = sb.AppendLine("{");
                _ = Emitter.EmitMapValueReader(sb, prop);
                _ = sb.AppendLine("break;");
                _ = sb.AppendLine("}");
            }
            _ = sb.AppendLine("default:");
            _ = sb.AppendLine("reader.ReadDataItem();");
            _ = sb.AppendLine("break;");
            _ = sb.AppendLine("}");

            _ = sb.AppendLine($"resultMap.Add(key, value);");
            _ = sb.AppendLine($"if (mapSize > 0) mapRemaining--;");
            _ = sb.AppendLine("}");

            // Skip break for indefinite maps
            _ = sb.AppendLine("if (isIndefiniteMap && reader.Buffer.Length > 0 && reader.Buffer[0] == 0xFF) reader.ReadDataItem();");
            _ = sb.AppendLine($"{metadata.FullyQualifiedName} result = new {metadata.FullyQualifiedName}(");

            for (int i = 0; i < metadata.Properties.Count; i++)
            {
                SerializablePropertyMetadata prop = metadata.Properties[i];
                string? key = isIntKey ? prop.PropertyKeyInt?.ToString(CultureInfo.InvariantCulture) : prop.PropertyKeyString;

                _ = sb.Append($"resultMap.TryGetValue({key}, out var {prop.PropertyName}Value) ? ({prop.PropertyTypeFullName}){prop.PropertyName}Value : default");
                _ = sb.AppendLine($"{(i == metadata.Properties.Count - 1 ? "" : ",")}");
            }

            _ = sb.AppendLine(");");

            // Set the IsIndefinite flag if we detected it
            _ = sb.AppendLine("if (isIndefiniteMap)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("    result.IsIndefinite = true;");
            _ = sb.AppendLine("}");

            _ = Emitter.EmitReaderValidationAndResult(sb, metadata, "result");
            return sb;
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = Emitter.EmitPreservedRawWriter(sb);
            _ = Emitter.EmitWriterValidation(sb, metadata);
            _ = Emitter.EmitTagWriter(sb, metadata.CborTag);
            _ = Emitter.EmitPropertyCountWriter(sb, metadata);
            bool isIntKey = metadata.Properties[0].PropertyKeyInt is not null;

            // Use indefinite if either attribute OR runtime flag is set
            _ = sb.AppendLine($"bool useIndefinite = {(metadata.IsIndefinite ? "true" : "false")} || data.IsIndefinite;");
            _ = sb.AppendLine("int _mapSize;");
            _ = sb.AppendLine("if (useIndefinite)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("    _mapSize = -1;");
            _ = sb.AppendLine("    writer.WriteBeginMap(-1);");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("else");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("    _mapSize = propCount;");
            _ = sb.AppendLine("    writer.WriteBeginMap(propCount);");
            _ = sb.AppendLine("}");

            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                if (prop.IsNullable)
                {
                    _ = sb.AppendLine($"{(isIntKey ? $"writer.WriteInt32(KeyMapping[\"{prop.PropertyName}\"])" : $"writer.WriteString(KeyMapping[\"{prop.PropertyName}\"])")};");
                    _ = Emitter.EmitSerializablePropertyWriter(sb, prop);
                }
                else
                {
                    if (prop.IsTypeNullable)
                    {
                        _ = sb.AppendLine($"if (data.{prop.PropertyName} is not null)");
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"{(isIntKey ? $"writer.WriteInt32(KeyMapping[\"{prop.PropertyName}\"])" : $"writer.WriteString(KeyMapping[\"{prop.PropertyName}\"])")};");
                        _ = Emitter.EmitSerializablePropertyWriter(sb, prop);
                        _ = sb.AppendLine("}");
                    }
                    else
                    {
                        _ = sb.AppendLine($"{(isIntKey ? $"writer.WriteInt32(KeyMapping[\"{prop.PropertyName}\"])" : $"writer.WriteString(KeyMapping[\"{prop.PropertyName}\"])")};");
                        _ = Emitter.EmitSerializablePropertyWriter(sb, prop);
                    }
                }
            }
            _ = sb.AppendLine($"writer.WriteEndMap(_mapSize);");

            return sb;
        }
    }
}
