using Chrysalis.Attributes;
using Chrysalis.Converters;

namespace Chrysalis.Types.Core;

[CborSerializable(Converter = typeof(CborBoolConverter))]
public record CborBool(bool Value) : RawCbor;