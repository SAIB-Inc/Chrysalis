using Chrysalis.Attributes;
using Chrysalis.Converters;
using Chrysalis.Types.Core;

namespace Chrysalis.Types.Custom.Test;

[CborSerializable(Converter = typeof(CborConstrConverter), Index = 1)]
public record CborConstrTestIndex1 : CborConstr;

[CborSerializable(Converter = typeof(CborConstrConverter), Index = 0)]
public record CborConstrTestWithParams(
    CborBytes ShortBytes,
    CborBoundedBytesTest LongBytes
) : CborConstr;

[CborSerializable(Converter = typeof(CborConstrConverter), Index = 1, IsDefinite = true)]
public record CborConstrTestDefinite : CborConstr;

[CborSerializable(Converter = typeof(CborConstrConverter), Index = 0)]
public record CborConstrTestNested(
    CborConstrTestWithParams WithParams,
    CborConstrTestDefinite Definite
) : CborConstr;