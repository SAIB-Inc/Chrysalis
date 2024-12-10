using Chrysalis.Cbor;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Cardano.Levvy.TokenV2.Types.Redeemers;

[CborConverter(typeof(UnionConverter))]
public record TokenRedeemer() : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record BorrowTokenAction() : TokenRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record RepayTokenAction() : TokenRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(2)]
public record ClaimTokenAction() : TokenRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(3)]
public record ForecloseTokenAction() : TokenRedeemer;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(4)]
public record CancelTokenAction() : TokenRedeemer;