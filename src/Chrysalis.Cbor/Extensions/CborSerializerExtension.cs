using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Extensions;

public static class CborSerializerExtensions
{
    public static ReadOnlySpan<byte> ToCbor<T>(this T value) where T : CborBase
        => CborSerializer.Serialize(value);

    public static T FromCbor<T>(this byte[] data) where T : CborBase
        => CborSerializer.Deserialize<T>(data);
}