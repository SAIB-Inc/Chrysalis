using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Converters;

public interface ICborConverter
{
    void Serialize(CborWriter writer, object value, CborOptions? options = null);
    object? Deserialize(CborReader reader, CborOptions? options = null);
}
