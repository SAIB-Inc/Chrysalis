using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Levvy.TokenV2;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(BorrowTokenAction),
    typeof(RepayTokenAction),
    typeof(ClaimTokenAction),
    typeof(ForecloseTokenAction),
    typeof(CancelTokenAction),
])]
public record TokenRedeemer() : RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record BorrowTokenAction() : TokenRedeemer;

[CborSerializable(CborType.Constr, Index = 1)]
public record RepayTokenAction() : TokenRedeemer;

[CborSerializable(CborType.Constr, Index = 2)]
public record ClaimTokenAction() : TokenRedeemer;

[CborSerializable(CborType.Constr, Index = 3)]
public record ForecloseTokenAction() : TokenRedeemer;

[CborSerializable(CborType.Constr, Index = 4)]
public record CancelTokenAction() : TokenRedeemer;
