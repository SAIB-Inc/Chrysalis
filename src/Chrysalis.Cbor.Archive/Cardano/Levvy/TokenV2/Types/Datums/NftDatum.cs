using Chrysalis.Cardano.Core.Types.Primitives;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.Types.Address;

namespace Chrysalis.Cardano.Levvy.TokenV2.Types.Datums;

[CborConverter(typeof(UnionConverter))]
public record NftDatum() : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record LendNftDatum(LendNftDetails LendNftDetails) : NftDatum;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record BorrowNftDatum(BorrowNftDetails BorrowNftDetails) : NftDatum;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(2)]
public record RepayNftDatum(RepayNftDetails RepayNftDetails) : NftDatum;


[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
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
) : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
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
) : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record RepayNftDetails(
    [CborProperty(0)]
    Address AdaOwner,

    [CborProperty(1)]
    CborUlong LoanAmount,

    [CborProperty(2)]
    CborUlong InterestAmount,

    [CborProperty(3)]
    OutputReference OutputReference
) : CborBase;