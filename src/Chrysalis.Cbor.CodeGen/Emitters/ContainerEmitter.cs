using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private sealed class ContainerEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata, bool useExistingReader)
        {
            if (metadata.Properties.Count is > 1 or < 1)
            {
                throw new InvalidOperationException($"Container types must have exactly one property. {metadata.FullyQualifiedName}");
            }

            SerializablePropertyMetadata prop = metadata.Properties[0];
            if (!useExistingReader)
            {
                _ = Emitter.EmitCborReaderInstance(sb, "data");
            }
            _ = Emitter.EmitTagReader(sb, metadata.CborTag, "tagIndex");
            string propName = $"{metadata.BaseIdentifier}{prop.PropertyName}";
            _ = Emitter.EmitSerializablePropertyReader(sb, prop, propName);
            _ = sb.AppendLine($"var result = new {metadata.FullyQualifiedName}({propName});");
            _ = Emitter.EmitReaderValidationAndResult(sb, metadata, "result", hasInputData: !useExistingReader);

            return sb;
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            if (metadata.Properties.Count is > 1 or < 1)
            {
                throw new InvalidOperationException($"Container types must have exactly one property. {metadata.FullyQualifiedName}");
            }

            _ = Emitter.EmitPreservedRawWriter(sb);
            _ = Emitter.EmitWriterValidation(sb, metadata);
            SerializablePropertyMetadata prop = metadata.Properties[0];
            _ = Emitter.EmitTagWriter(sb, metadata.CborTag);
            _ = Emitter.EmitSerializablePropertyWriter(sb, prop);

            return sb;
        }
    }
}
