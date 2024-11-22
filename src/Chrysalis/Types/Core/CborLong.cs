using Chrysalis.Attributes;
using Chrysalis.Converters;

namespace Chrysalis.Types.Core;

[CborSerializable(Converter = typeof(CborLongConverter))]
public record CborLong(long Value) : RawCbor;