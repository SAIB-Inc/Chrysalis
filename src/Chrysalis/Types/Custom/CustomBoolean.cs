
using Chrysalis.Attributes;
using Chrysalis.Converters;

namespace Chrysalis.Types.Custom;

[CborConverter(typeof(UnionConverter<CardanoBool>))]
public interface CardanoBool : ICbor;

[CborIndex(1)]
[CborDefinite]
public record CborTrue : CborConstr, CardanoBool;

[CborIndex(0)]
[CborDefinite]
public record CborFalse : CborConstr, CardanoBool;