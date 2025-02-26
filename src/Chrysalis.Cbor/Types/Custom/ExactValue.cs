using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;

namespace Chrysalis.Cbor.Types.Custom;

[CborConverter(typeof(ExactValueConverter))]
public record ExactValue<T>(T Value) : CborBase where T : CborBase;