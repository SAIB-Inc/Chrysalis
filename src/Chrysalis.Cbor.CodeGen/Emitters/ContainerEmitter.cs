using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private class ContainerEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitCborDeserializer(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            if (metadata.Properties.Count > 1 || metadata.Properties.Count < 1)
                throw new InvalidOperationException($"Container types must have exactly one property. {metadata.FullyQualifiedName}");

            SerializablePropertyMetadata prop = metadata.Properties[0];
            Emitter.EmitNewCborReader(sb, "data");
            Emitter.EmitTagReader(sb, metadata.CborTag, "tagIndex");
            string propName = $"{metadata.BaseIdentifier}{prop.PropertyName}";
            Emitter.EmitGenericCborReader(sb, prop, propName);
            sb.AppendLine($"var result = new {metadata.FullyQualifiedName}({propName});");
            Emitter.EmitReadFinalizer(sb, metadata, "result");

            return sb;
        }

        public StringBuilder EmitCborSerializer(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            return sb;
        }
    }
}