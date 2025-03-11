using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Levvy.TokenV2.Types.Redeemers;

[CborConverter(typeof(UnionConverter))]
public partial record TokenRedeemer() : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record BorrowTokenAction() : TokenRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 1)]
public partial record RepayTokenAction() : TokenRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 2)]
public partial record ClaimTokenAction() : TokenRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 3)]
public partial record ForecloseTokenAction() : TokenRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 4)]
public partial record CancelTokenAction() : TokenRedeemer;