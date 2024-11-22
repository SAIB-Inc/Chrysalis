using Chrysalis.Attributes;
using Chrysalis.Converters;

namespace Chrysalis.Types.Core;

[CborSerializable(Converter = typeof(CborUlongConverter))]
public record CborUlong(ulong Value) : RawCbor;