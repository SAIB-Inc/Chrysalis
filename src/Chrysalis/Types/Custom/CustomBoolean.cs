
using Chrysalis.Attributes;
using Chrysalis.Converters;

namespace Chrysalis.Types.Custom;

[CborConverter(typeof(UnionConverter))]
public interface ICardanoBool : ICbor;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
[CborDefinite]
public record CborTrue : Cbor, ICardanoBool;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
[CborDefinite]
public record CborFalse : Cbor, ICardanoBool;

public record CborInheritedTrue : CborTrue;