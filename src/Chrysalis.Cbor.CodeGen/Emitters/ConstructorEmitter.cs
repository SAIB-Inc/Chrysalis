using System.Text;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private class ConstructorEmitter : ICborSerializerEmitter
    {
        public StringBuilder EmitCborDeserializer(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            int constrIndex = ResolveTag(metadata.CborIndex);
            Emitter.EmitNewCborReader(sb, "data");
            Emitter.EmitTagReader(sb, metadata.CborTag, "tagIndex");
            Emitter.EmitTagReader(sb, constrIndex, "constrIndex");
            Emitter.EmitListRead(sb, metadata);

            return sb;
        }

        public StringBuilder EmitCborSerializer(StringBuilder sb, SerializableTypeMetadata metadata)
        {
            return sb;
        }

        private static int ResolveTag(int? index)
        {
            if (index is null || index < 0)
            {
                return -1;
            }
            else
            {
                int finalIndex = index > 6 ? 1280 - 7 : 121;
                return finalIndex + (index ?? 0);
            }
        }
    }
}