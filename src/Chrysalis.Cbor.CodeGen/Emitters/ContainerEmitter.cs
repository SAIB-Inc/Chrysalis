using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private class ContainerEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            if (metadata.Properties.Count > 1 || metadata.Properties.Count < 1)
                throw new InvalidOperationException($"Container types must have exactly one property. {metadata.FullyQualifiedName}");

            SerializablePropertyMetadata prop = metadata.Properties[0];
            Emitter.EmitCborReaderInstance(sb, "data");
            Emitter.EmitTagReader(sb, metadata.CborTag, "tagIndex");
            string propName = $"{metadata.BaseIdentifier}{prop.PropertyName}";
            Emitter.EmitSerializablePropertyReader(sb, prop, propName);
            sb.AppendLine($"var result = new {metadata.FullyQualifiedName}({propName});");
            Emitter.EmitReaderValidationAndResult(sb, metadata, "result");

            return sb;
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            if (metadata.Properties.Count > 1 || metadata.Properties.Count < 1)
                throw new InvalidOperationException($"Container types must have exactly one property. {metadata.FullyQualifiedName}");

            SerializablePropertyMetadata prop = metadata.Properties[0];
            Emitter.EmitTagWriter(sb, metadata.CborTag);
            Emitter.EmitSerializablePropertyWriter(sb, prop);

            return sb;
        }
    }
}