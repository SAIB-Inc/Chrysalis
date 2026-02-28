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

            // Read the map and check if it's indefinite
            _ = sb.AppendLine("int? mapLength = reader.ReadStartMap();");
            _ = sb.AppendLine("bool isIndefiniteMap = !mapLength.HasValue;");

            _ = sb.AppendLine("while (reader.PeekState() != CborReaderState.EndMap)");
            _ = sb.AppendLine("{");

            _ = isIntKey
                ? sb.AppendLine($"int key = reader.ReadInt32();")
                : sb.AppendLine($"string key = reader.ReadTextString();");

            _ = sb.AppendLine($"object value = null;");

            _ = Emitter.EmitGenericReader(sb, "TypeMapping[key]", "value");

            _ = sb.AppendLine($"resultMap.Add(key, value);");
            _ = sb.AppendLine("}");

            // Read the end map marker
            _ = sb.AppendLine("reader.ReadEndMap();");
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
            _ = sb.AppendLine("if (useIndefinite)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("    writer.WriteStartMap(null);");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("else");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("    writer.WriteStartMap(propCount);");
            _ = sb.AppendLine("}");

            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                if (prop.IsNullable)
                {
                    _ = sb.AppendLine($"{(isIntKey ? $"writer.WriteInt32(KeyMapping[\"{prop.PropertyName}\"])" : $"writer.WriteTextString(KeyMapping[\"{prop.PropertyName}\"])")};");
                    _ = Emitter.EmitSerializablePropertyWriter(sb, prop);
                }
                else
                {
                    if (prop.IsTypeNullable)
                    {
                        _ = sb.AppendLine($"if (data.{prop.PropertyName} is not null)");
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine($"{(isIntKey ? $"writer.WriteInt32(KeyMapping[\"{prop.PropertyName}\"])" : $"writer.WriteTextString(KeyMapping[\"{prop.PropertyName}\"])")};");
                        _ = Emitter.EmitSerializablePropertyWriter(sb, prop);
                        _ = sb.AppendLine("}");
                    }
                    else
                    {
                        _ = sb.AppendLine($"{(isIntKey ? $"writer.WriteInt32(KeyMapping[\"{prop.PropertyName}\"])" : $"writer.WriteTextString(KeyMapping[\"{prop.PropertyName}\"])")};");
                        _ = Emitter.EmitSerializablePropertyWriter(sb, prop);
                    }
                }
            }
            _ = sb.AppendLine($"writer.WriteEndMap();");

            return sb;
        }
    }
}
