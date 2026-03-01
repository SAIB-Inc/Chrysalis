using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private sealed class ConstructorEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata, bool useExistingReader)
        {
            int constrIndex = Emitter.ResolveTag(metadata.CborIndex);
            if (!useExistingReader)
            {
                _ = Emitter.EmitCborReaderInstance(sb, "data");
            }
            _ = Emitter.EmitTagReader(sb, metadata.CborTag, "tagIndex");
            _ = Emitter.EmitTagReader(sb, constrIndex, "constrIndex");
            _ = Emitter.EmitCustomListReader(sb, metadata);

            return sb;
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = Emitter.EmitPreservedRawWriter(sb);
            _ = Emitter.EmitWriterValidation(sb, metadata);
            _ = Emitter.EmitTagWriter(sb, metadata.CborTag);

            _ = metadata.CborIndex is null or < 0
                ? sb.AppendLine("writer.WriteTag((CborTag)data.ConstrIndex);")
                : sb.AppendLine($"writer.WriteTag((CborTag){Emitter.ResolveTag(metadata.CborIndex)});");

            _ = Emitter.EmitCustomListWriter(sb, metadata);

            return sb;
        }
    }
}
