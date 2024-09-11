using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core;
using Chrysalis.Cbor;
using Address = Chrysalis.Cardano.Models.Plutus.Address;

namespace Chrysalis.Cardano.Models.Levvy;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(LendTokenDatum), typeof(BorrowTokenDatum), typeof(RepayTokenDatum)])]
public record TokenDatum() : ICbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record LendTokenDatum(LendTokenDetails LendTokenDetails) : TokenDatum;

[CborSerializable(CborType.Constr, Index = 1)]
public record BorrowTokenDatum(BorrowTokenDetails BorrowTokenDetails) : TokenDatum;

[CborSerializable(CborType.Constr, Index = 2)]
public record RepayTokenDatum(RepayTokenDetails RepayTokenDetails) : TokenDatum;

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
) : ICbor;


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
) : ICbor;

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
) : ICbor;