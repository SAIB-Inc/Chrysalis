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
            sb.AppendLine($"reader.ReadStartMap();");
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
            sb.AppendLine($"reader.ReadEndMap();");
            sb.AppendLine($"{metadata.FullyQualifiedName} result = new {metadata.FullyQualifiedName}(");

            for (int i = 0; i < metadata.Properties.Count; i++)
            {
                SerializablePropertyMetadata prop = metadata.Properties[i];
                string? key = isIntKey ? prop.PropertyKeyInt?.ToString() : prop.PropertyKeyString;

                sb.Append($"resultMap.TryGetValue({key}, out var {prop.PropertyName}Value) ? ({prop.PropertyTypeFullName}){prop.PropertyName}Value : default");
                sb.AppendLine($"{(i == metadata.Properties.Count - 1 ? "" : ",")}");
            }

            sb.AppendLine(");");

            Emitter.EmitReaderValidationAndResult(sb, metadata, "result");
            return sb;
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            Emitter.EmitTagWriter(sb, metadata.CborTag);
            bool isIntKey = metadata.Properties[0].PropertyKeyInt is not null;

            sb.AppendLine($"writer.WriteStartMap(null);");
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