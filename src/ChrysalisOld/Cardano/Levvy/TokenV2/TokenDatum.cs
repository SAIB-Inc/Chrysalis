using Chrysalis.Cardano.Cbor;
using Chrysalis.Cardano.Plutus;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Levvy;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(LendTokenDatum), typeof(BorrowTokenDatum), typeof(RepayTokenDatum)])]
public record TokenDatum() : RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record LendTokenDatum(LendTokenDetails LendTokenDetails) : TokenDatum;

[CborSerializable(CborType.Constr, Index = 1)]
public record BorrowTokenDatum(BorrowTokenDetails BorrowTokenDetails) : TokenDatum;

[CborSerializable(CborType.Constr, Index = 2)]
public record RepayTokenDatum(RepayTokenDetails RepayTokenDetails) : TokenDatum;

[CborSerializable(CborType.Constr, Index = 0)]
public record LendTokenDetails(
    [CborProperty(0)]
    Address AdaOwner,

    [CborProperty(1)]
    CborBytes PolicyId,

    [CborProperty(2)]
    CborBytes AssetName,

    [CborProperty(3)]
    CborUlong TokenAmount,

    [CborProperty(4)]
    CborUlong LoanAmount,

    [CborProperty(5)]
    CborUlong InterestAmount,

    [CborProperty(6)]
    CborUlong LoanDuration,

    [CborProperty(7)]
    OutputReference OutputReference
) : RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record BorrowTokenDetails(
    [CborProperty(0)]
    Address AdaOwner,

    [CborProperty(1)]
    Address AssetOwner,

    [CborProperty(2)]
    CborBytes PolicyId,

    [CborProperty(3)]
    CborBytes AssetName,

    [CborProperty(4)]
    CborUlong TokenAmount,

    [CborProperty(5)]
    CborUlong LoanAmount,

    [CborProperty(6)]
    CborUlong InterestAmount,

    [CborProperty(7)]
    PosixTime LoanEndTime,

    [CborProperty(8)]
    OutputReference OutputReference
) : RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record RepayTokenDetails(
    [CborProperty(0)]
    Address AdaOwner,

    [CborProperty(1)]
    CborUlong TokenAmount,

    [CborProperty(2)]
    CborUlong LoanAmount,

    [CborProperty(3)]
    CborUlong InterestAmount,

    [CborProperty(4)]
    OutputReference OutputReference
) : RawCbor;