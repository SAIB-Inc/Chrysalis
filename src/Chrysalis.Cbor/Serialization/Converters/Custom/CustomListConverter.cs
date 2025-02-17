using System.Formats.Cbor;
using Chrysalis.Cbor.Serialization.Registry;
using Chrysalis.Cbor.Utils;

namespace Chrysalis.Cbor.Serialization.Converters.Custom;

public sealed class CustomListConverter : ICborConverter
{
    public object? Read(CborReader reader, CborOptions options)
    {
        return CustomListSerializationUtil.Read(reader, options);
    }

    public void Write(CborWriter writer, object? value, CborOptions options)
    {

    }
}