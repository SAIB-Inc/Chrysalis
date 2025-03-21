using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private class UnionEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            sb.AppendLine($"List<string> errors = [];");
            foreach (var childType in metadata.ChildTypes)
            {
                sb.AppendLine($"try");
                sb.AppendLine("{");
                sb.AppendLine($"return ({metadata.FullyQualifiedName}){childType.FullyQualifiedName}.Read(data);");
                sb.AppendLine("}");
                sb.AppendLine($"catch (Exception ex)");
                sb.AppendLine("{");
                sb.AppendLine($"errors.Add(ex.Message);");
                sb.AppendLine("}");
            }

            sb.AppendLine($"throw new Exception(\"Union deserialization failed. {metadata.FullyQualifiedName} \" + string.Join(\"\\n\", errors));");

            return sb;
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            sb.AppendLine("switch (data.CborTypeName)");
            sb.AppendLine("{");
            foreach (var childType in metadata.ChildTypes)
            {
                sb.AppendLine($"case \"{childType.FullyQualifiedName}\":");
                sb.AppendLine($"{childType.FullyQualifiedName}.Write(writer, ({childType.FullyQualifiedName})data);");
                sb.AppendLine($"break;");
            }
            sb.AppendLine($"default:");
            sb.AppendLine($"throw new Exception(\"Union serialization failed. {metadata.FullyQualifiedName} \");");
            sb.AppendLine("}");
            return sb;
        }
    }
}