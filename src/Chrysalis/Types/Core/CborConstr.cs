using Chrysalis.Attributes;
using Chrysalis.Converters;

namespace Chrysalis.Types.Core;

[CborSerializable(Converter = typeof(CborConstrConverter), Index = 0)]
public record CborConstr() : RawCbor;