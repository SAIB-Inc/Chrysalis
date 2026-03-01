using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private sealed class ListEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata, bool useExistingReader)
        {
            if (!useExistingReader)
            {
                _ = Emitter.EmitCborReaderInstance(sb, "data");
            }
            _ = Emitter.EmitTagReader(sb, metadata.CborTag, "tagIndex");
            _ = Emitter.EmitCustomListReader(sb, metadata);
            return sb;
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            _ = Emitter.EmitPreservedRawWriter(sb);
            _ = Emitter.EmitWriterValidation(sb, metadata);
            _ = Emitter.EmitTagWriter(sb, metadata.CborTag);
            _ = Emitter.EmitCustomListWriter(sb, metadata);
            return sb;
        }
    }
}
