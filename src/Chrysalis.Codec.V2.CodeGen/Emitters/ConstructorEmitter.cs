using System.Text;

namespace Chrysalis.Codec.V2.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private sealed class ConstructorEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = Emitter.EmitCborReaderInstance(sb, "data");

            if (metadata.IsRecordStruct)
            {
                _ = Emitter.EmitLazyListReader(sb, metadata);
            }
            else
            {
                int constrIndex = Emitter.ResolveTag(metadata.CborIndex);
                _ = Emitter.EmitTagReader(sb, metadata.CborTag, "tagIndex");
                _ = Emitter.EmitTagReader(sb, constrIndex, "constrIndex");
                _ = Emitter.EmitCustomListReader(sb, metadata);
            }

            return sb;
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = Emitter.EmitPreservedRawWriter(sb);
            _ = Emitter.EmitWriterValidation(sb, metadata);
            _ = Emitter.EmitTagWriter(sb, metadata.CborTag);

            _ = metadata.CborIndex is null or < 0
                ? sb.AppendLine("writer.WriteSemanticTag((ulong)data.ConstrIndex);")
                : sb.AppendLine($"writer.WriteSemanticTag((ulong){Emitter.ResolveTag(metadata.CborIndex)});");

            _ = Emitter.EmitCustomListWriter(sb, metadata);

            return sb;
        }
    }
}
