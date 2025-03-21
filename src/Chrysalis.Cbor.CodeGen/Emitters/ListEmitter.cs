using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private class ListEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitReader(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            Emitter.EmitCborReaderInstance(sb, "data");
            Emitter.EmitTagReader(sb, metadata.CborTag, "tagIndex");
            Emitter.EmitCustomListReader(sb, metadata);
            return sb;
        }

        public StringBuilder EmitWriter(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            Emitter.EmitTagWriter(sb, metadata.CborTag);
            Emitter.EmitCustomListWriter(sb, metadata);
            return sb;
        }
    }
}