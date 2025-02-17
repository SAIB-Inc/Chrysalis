using System.Formats.Cbor;

namespace Chrysalis.Cbor.Serialization.Converters.Primitives;

public sealed class ConstrConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        throw new NotImplementedException();
    }

    public void Write(CborWriter writer, object? value, CborOptions options)
    {
        throw new NotImplementedException();
    }
}