using Chrysalis.Cardano.Core.Types.Primitives;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.Types.Address;

namespace Chrysalis.Cardano.Levvy.TokenV2.Types.Datums;

[CborConverter(typeof(UnionConverter))]
public record TokenDatum() : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record LendTokenDatum(LendTokenDetails LendTokenDetails) : TokenDatum;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record BorrowTokenDatum(BorrowTokenDetails BorrowTokenDetails) : TokenDatum;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(2)]
public record RepayTokenDatum(RepayTokenDetails RepayTokenDetails) : TokenDatum;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
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
) : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
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
) : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
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
) : CborBase;