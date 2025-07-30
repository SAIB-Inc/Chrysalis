using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private class MapEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            Emitter.EmitCborReaderInstance(sb, "data");
            Emitter.EmitTagReader(sb, metadata.CborTag, "tagIndex");
            bool isIntKey = metadata.Properties[0].PropertyKeyInt is not null;

            sb.AppendLine($"Dictionary<{(isIntKey ? "int" : "string")}, object> resultMap = [];");
            
            // Read the map and check if it's indefinite
            sb.AppendLine("int? mapLength = reader.ReadStartMap();");
            sb.AppendLine("bool isIndefiniteMap = !mapLength.HasValue;");
            
            sb.AppendLine("while (reader.PeekState() != CborReaderState.EndMap)");
            sb.AppendLine("{");

            if (isIntKey)
            {
                sb.AppendLine($"int key = reader.ReadInt32();");
            }
            else
            {
                sb.AppendLine($"string key = reader.ReadTextString();");
            }

            sb.AppendLine($"object value = null;");

            Emitter.EmitGenericReader(sb, "TypeMapping[key]", "value");

            sb.AppendLine($"resultMap.Add(key, value);");
            sb.AppendLine("}");
            
            // Read the end map marker
            sb.AppendLine("reader.ReadEndMap();");
            sb.AppendLine($"{metadata.FullyQualifiedName} result = new {metadata.FullyQualifiedName}(");

            for (int i = 0; i < metadata.Properties.Count; i++)
            {
                SerializablePropertyMetadata prop = metadata.Properties[i];
                string? key = isIntKey ? prop.PropertyKeyInt?.ToString() : prop.PropertyKeyString;

                sb.Append($"resultMap.TryGetValue({key}, out var {prop.PropertyName}Value) ? ({prop.PropertyTypeFullName}){prop.PropertyName}Value : default");
                sb.AppendLine($"{(i == metadata.Properties.Count - 1 ? "" : ",")}");
            }

            sb.AppendLine(");");
            
            // Set the IsIndefinite flag if we detected it
            sb.AppendLine("if (isIndefiniteMap)");
            sb.AppendLine("{");
            sb.AppendLine("    result.IsIndefinite = true;");
            sb.AppendLine("}");

            Emitter.EmitReaderValidationAndResult(sb, metadata, "result");
            return sb;
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            Emitter.EmitPreservedRawWriter(sb);
            Emitter.EmitWriterValidation(sb, metadata);
            Emitter.EmitTagWriter(sb, metadata.CborTag);
            Emitter.EmitPropertyCountWriter(sb, metadata);
            bool isIntKey = metadata.Properties[0].PropertyKeyInt is not null;

            // Use indefinite if either attribute OR runtime flag is set
            sb.AppendLine($"bool useIndefinite = {(metadata.IsIndefinite ? "true" : "false")} || data.IsIndefinite;");
            sb.AppendLine("if (useIndefinite)");
            sb.AppendLine("{");
            sb.AppendLine("    writer.WriteStartMap(null);");
            sb.AppendLine("}");
            sb.AppendLine("else");
            sb.AppendLine("{");
            sb.AppendLine("    writer.WriteStartMap(propCount);");
            sb.AppendLine("}");

            foreach (var prop in metadata.Properties)
            {
                if (prop.IsNullable)
                {
                    sb.AppendLine($"{(isIntKey ? $"writer.WriteInt32(KeyMapping[\"{prop.PropertyName}\"])" : $"writer.WriteTextString(KeyMapping[\"{prop.PropertyName}\"])")};");
                    Emitter.EmitSerializablePropertyWriter(sb, prop);
                }
                else
                {
                    if (prop.IsTypeNullable)
                    {
                        sb.AppendLine($"if (data.{prop.PropertyName} is not null)");
                        sb.AppendLine("{");
                        sb.AppendLine($"{(isIntKey ? $"writer.WriteInt32(KeyMapping[\"{prop.PropertyName}\"])" : $"writer.WriteTextString(KeyMapping[\"{prop.PropertyName}\"])")};");
                        Emitter.EmitSerializablePropertyWriter(sb, prop);
                        sb.AppendLine("}");
                    }
                    else
                    {
                        sb.AppendLine($"{(isIntKey ? $"writer.WriteInt32(KeyMapping[\"{prop.PropertyName}\"])" : $"writer.WriteTextString(KeyMapping[\"{prop.PropertyName}\"])")};");
                        Emitter.EmitSerializablePropertyWriter(sb, prop);
                    }
                }
            }
            sb.AppendLine($"writer.WriteEndMap();");

            return sb;
        }
    }
}