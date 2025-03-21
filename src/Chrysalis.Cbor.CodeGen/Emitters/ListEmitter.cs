using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private class ListEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitCborDeserializer(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            Emitter.EmitNewCborReader(sb, "data");
            Emitter.EmitTagReader(sb, metadata.CborTag, "tagIndex");
            Emitter.EmitListRead(sb, metadata);
            return sb;
        }

        public StringBuilder EmitCborSerializer(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            return sb;
        }
    }
}