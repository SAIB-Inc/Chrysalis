using System.Globalization;
using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private sealed class MapEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata, bool useExistingReader)
        {
            if (!useExistingReader)
            {
                _ = Emitter.EmitCborReaderInstance(sb, "data");
            }
            _ = Emitter.EmitTagReader(sb, metadata.CborTag, "tagIndex");
            bool isIntKey = metadata.Properties[0].PropertyKeyInt is not null;
            bool assumeNoPreserveRaw = useExistingReader && metadata.ShouldPreserveRaw;

            // Read the map and check if it's indefinite
            _ = sb.AppendLine("int? mapLength = reader.ReadStartMap();");
            _ = sb.AppendLine("bool isIndefiniteMap = !mapLength.HasValue;");

            // Deserialize directly into strongly typed locals to avoid map boxing/object dispatch.
            Dictionary<string, string> propMapping = [];
            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                string propName = $"{metadata.BaseIdentifier}{prop.PropertyName}";
                propMapping[prop.PropertyName] = propName;
                _ = sb.AppendLine($"{prop.PropertyTypeFullName} {propName} = default{(prop.PropertyType.Contains("?") ? "" : "!")};");
            }

            _ = sb.AppendLine("while (reader.PeekState() != CborReaderState.EndMap)");
            _ = sb.AppendLine("{");

            _ = isIntKey
                ? sb.AppendLine($"int key = reader.ReadInt32();")
                : sb.AppendLine($"string key = reader.ReadTextString();");

            _ = sb.AppendLine("switch (key)");
            _ = sb.AppendLine("{");
            foreach (SerializablePropertyMetadata prop in metadata.Properties)
            {
                string key = isIntKey
                    ? prop.PropertyKeyInt?.ToString(CultureInfo.InvariantCulture) ?? "0"
                    : ToCSharpStringLiteral(prop.PropertyKeyString ?? string.Empty);
                string propName = propMapping[prop.PropertyName];

                _ = sb.AppendLine($"case {key}:");
                _ = sb.AppendLine("{");
                _ = Emitter.EmitPrimitiveOrObjectReader(sb, prop, propName, assumeNoPreserveRaw);
                _ = sb.AppendLine("break;");
                _ = sb.AppendLine("}");
            }

            _ = sb.AppendLine("default:");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine($"throw new Exception(\"Unknown CBOR map key while reading {metadata.FullyQualifiedName}\");");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("}");
            _ = sb.AppendLine("}");

            // Read the end map marker
            _ = sb.AppendLine("reader.ReadEndMap();");
            _ = sb.AppendLine($"{metadata.FullyQualifiedName} result = new {metadata.FullyQualifiedName}(");

            for (int i = 0; i < metadata.Properties.Count; i++)
            {
                SerializablePropertyMetadata prop = metadata.Properties[i];
                _ = sb.Append($"{propMapping[prop.PropertyName]}");
                _ = sb.AppendLine($"{(i == metadata.Properties.Count - 1 ? "" : ",")}");
            }

            _ = sb.AppendLine(");");

            // Set the IsIndefinite flag if we detected it
            _ = sb.AppendLine("if (isIndefiniteMap)");
            _ = sb.AppendLine("{");
            _ = sb.AppendLine("    result.IsIndefinite = true;");
            _ = sb.AppendLine("}");

            _ = Emitter.EmitReaderValidationAndResult(sb, metadata, "result", hasInputData: !useExistingReader);
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
                string mapKeyWriterLine = isIntKey
                    ? $"writer.WriteInt32({prop.PropertyKeyInt?.ToString(CultureInfo.InvariantCulture) ?? "0"});"
                    : $"writer.WriteTextString({ToCSharpStringLiteral(prop.PropertyKeyString ?? string.Empty)});";

                if (prop.IsNullable)
                {
                    _ = sb.AppendLine(mapKeyWriterLine);
                    _ = Emitter.EmitSerializablePropertyWriter(sb, prop);
                }
                else
                {
                    if (prop.IsTypeNullable)
                    {
                        _ = sb.AppendLine($"if (data.{prop.PropertyName} is not null)");
                        _ = sb.AppendLine("{");
                        _ = sb.AppendLine(mapKeyWriterLine);
                        _ = Emitter.EmitSerializablePropertyWriter(sb, prop);
                        _ = sb.AppendLine("}");
                    }
                    else
                    {
                        _ = sb.AppendLine(mapKeyWriterLine);
                        _ = Emitter.EmitSerializablePropertyWriter(sb, prop);
                    }
                }
            }
            _ = sb.AppendLine($"writer.WriteEndMap();");

            return sb;
        }

        private static string ToCSharpStringLiteral(string value)
        {
            return "\"" + value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t") + "\"";
        }
    }
}
