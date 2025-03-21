using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private class UnionEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitCborDeserializer(StringBuilder sb, SerializableTypeMetadata metadata)
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

        public StringBuilder EmitCborSerializer(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            return sb;
        }
    }
}