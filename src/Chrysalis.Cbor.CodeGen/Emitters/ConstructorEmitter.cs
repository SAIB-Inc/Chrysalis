using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private class ConstructorEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            int constrIndex = Emitter.ResolveTag(metadata.CborIndex);
            Emitter.EmitCborReaderInstance(sb, "data");
            Emitter.EmitTagReader(sb, metadata.CborTag, "tagIndex");
            Emitter.EmitTagReader(sb, constrIndex, "constrIndex");
            Emitter.EmitCustomListReader(sb, metadata);

            return sb;
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            Emitter.EmitTagWriter(sb, metadata.CborTag);

            if (metadata.CborIndex is null || metadata.CborIndex < 0)
            {
                sb.AppendLine("writer.WriteTag((CborTag)data.ConstrIndex);");
            }
            else
            {
                sb.AppendLine($"writer.WriteTag((CborTag){Emitter.ResolveTag(metadata.CborIndex)});");
            }

            Emitter.EmitCustomListWriter(sb, metadata);

            return sb;
        }
    }
}