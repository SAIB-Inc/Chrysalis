using System.Formats.Cbor;

namespace Chrysalis.Cbor.Serialization;

public interface ICborConverter
{
    void Write(CborWriter writer, object? value, CborOptions options);
    object? Read(CborReader reader, CborOptions options);
}