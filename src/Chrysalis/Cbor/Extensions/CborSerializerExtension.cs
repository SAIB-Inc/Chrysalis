using Chrysalis.Cbor.Converters;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Extensions;

public static class CborSerializerExtensions
{
    public static byte[] ToCbor<T>(this T value) where T : CborBase
        => CborSerializer.Serialize(value);

    public static T FromCbor<T>(this byte[] data) where T : CborBase
        => CborSerializer.Deserialize<T>(data);
}