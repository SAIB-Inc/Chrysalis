using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private class MapEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitCborDeserializer(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            Emitter.EmitNewCborReader(sb, "data");
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
            Emitter.EmitSerializationRead(sb, "TypeMapping[key]", "value");
            sb.AppendLine($"resultMap.Add(key, value);");
            sb.AppendLine("}");
            sb.AppendLine($"reader.ReadEndMap();");

            // Add constructor parameters
            sb.AppendLine($"{metadata.FullyQualifiedName} result = new {metadata.FullyQualifiedName}(");
            for (int i = 0; i < metadata.Properties.Count; i++)
            {
                var prop = metadata.Properties[i];
                string? key = isIntKey
                    ? prop.PropertyKeyInt?.ToString()
                    : prop.PropertyKeyString;

                sb.Append($"    resultMap.TryGetValue({key}, out var {prop.PropertyName}Value) ? ({prop.PropertyTypeFullName}){prop.PropertyName}Value : default");

                if (i < metadata.Properties.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }
            sb.AppendLine(");");

            Emitter.EmitReadFinalizer(sb, metadata, "result");
            return sb;
        }

        public StringBuilder EmitCborSerializer(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            return sb;
        }
    }
}