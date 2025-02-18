using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Levvy.TokenV2.Types.Redeemers;

[CborConverter(typeof(UnionConverter))]
public record TokenRedeemer() : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public record BorrowTokenAction() : TokenRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 1)]
public record RepayTokenAction() : TokenRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 2)]
public record ClaimTokenAction() : TokenRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 3)]
public record ForecloseTokenAction() : TokenRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 4)]
public record CancelTokenAction() : TokenRedeemer;