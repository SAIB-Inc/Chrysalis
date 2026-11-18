using Chrysalis.Codec.Serialization;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Types.Cardano.Core.Protocol;

internal static class ProtocolUpdateWire
{
    internal static IProtocolParamUpdate Read(ReadOnlyMemory<byte> data, out int consumed)
    {
        // Common keys have an identical representation. Babbage-only and Conway-only
        // fields select their schema; a mixture is not a known update in either era.
        CborReader reader = new(data.Span);
        reader.ReadBeginMap();
        int remaining = reader.ReadSize();
        bool babbage = false;
        bool conway = false;
        while (remaining > 0 || (remaining < 0 && !reader.Buffer.IsEmpty && reader.Buffer[0] != 0xFF))
        {
            int offset = data.Length - reader.Buffer.Length;
            int key = reader.ReadInt32();
            babbage |= key is 12 or 13 or 14 or 15;
            conway |= key is >= 25 and <= 33;
            if (babbage && conway)
            {
                throw CborError.Decode(data, offset, nameof(IProtocolParamUpdate), "one era's update fields", key);
            }
            reader.SkipDataItem();
            remaining -= remaining > 0 ? 1 : 0;
        }
        return babbage ? BabbageProtocolParamUpdate.Read(data, out consumed) : ConwayProtocolParamUpdate.Read(data, out consumed);
    }
}
