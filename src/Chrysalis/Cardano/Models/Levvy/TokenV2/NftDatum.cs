using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Plutus;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Levvy.TokenV2;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(LendNftDatum), typeof(BorrowNftDatum), typeof(RepayNftDatum)])]
public record NftDatum() : RawCbor;


[CborSerializable(CborType.Constr, Index = 0)]
public record LendNftDatum(LendNftDetails LendNftDetails) : NftDatum;

[CborSerializable(CborType.Constr, Index = 1)]
public record BorrowNftDatum(BorrowNftDetails BorrowNftDetails) : NftDatum;

[CborSerializable(CborType.Constr, Index = 2)]
public record RepayNftDatum(RepayNftDetails RepayNftDetails) : NftDatum;


[CborSerializable(CborType.Constr, Index = 0)]
public record LendNftDetails(
    [CborProperty(0)]
    Address AdaOwner,

    [CborProperty(1)]
    CborBytes PolicyId,

    [CborProperty(2)]
    CborUlong LoanAmount,

    [CborProperty(3)]
    CborUlong InterestAmount,

    [CborProperty(4)]
    CborUlong LoanDuration
) : RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record BorrowNftDetails(
    [CborProperty(0)]
    Address AdaOwner,

    [CborProperty(1)]
    Address AssetOwner,

    [CborProperty(2)]
    CborBytes PolicyId,

    [CborProperty(3)]
    CborBytes AssetName,

    [CborProperty(4)]
    CborUlong LoanAmount,

    [CborProperty(5)]
    CborUlong InterestAmount,

    [CborProperty(6)]
    PosixTime LoanEndTime,

    [CborProperty(7)]
    OutputReference OutputReference
) : RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record RepayNftDetails(
    [CborProperty(0)]
    Address AdaOwner,

    [CborProperty(1)]
    CborUlong LoanAmount,

    [CborProperty(2)]
    CborUlong InterestAmount,

    [CborProperty(3)]
    OutputReference OutputReference
) : RawCbor;