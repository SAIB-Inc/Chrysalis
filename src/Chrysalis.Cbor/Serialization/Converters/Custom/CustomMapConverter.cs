using System.Formats.Cbor;

namespace Chrysalis.Cbor.Serialization.Converters.Custom;

public sealed class CustomMapConverter : ICborConverter
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