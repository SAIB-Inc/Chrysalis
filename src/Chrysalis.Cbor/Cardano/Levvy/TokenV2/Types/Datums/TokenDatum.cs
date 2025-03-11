using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Types.Primitives;
using Chrysalis.Cbor.Plutus.Types;
using Chrysalis.Cbor.Plutus.Types.Address;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Levvy.TokenV2.Types.Datums;

[CborConverter(typeof(UnionConverter))]
public partial record TokenDatum() : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record LendTokenDatum(LendTokenDetails LendTokenDetails) : TokenDatum;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 1)]
public partial record BorrowTokenDatum(BorrowTokenDetails BorrowTokenDetails) : TokenDatum;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 2)]
public partial record RepayTokenDatum(RepayTokenDetails RepayTokenDetails) : TokenDatum;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record LendTokenDetails(
    [CborIndex(0)]
    Address AdaOwner,

    [CborIndex(1)]
    CborBytes PolicyId,

    [CborIndex(2)]
    CborBytes AssetName,

    [CborIndex(3)]
    CborUlong TokenAmount,

    [CborIndex(4)]
    CborUlong LoanAmount,

    [CborIndex(5)]
    CborUlong InterestAmount,

    [CborIndex(6)]
    CborUlong LoanDuration,

    [CborIndex(7)]
    OutputReference OutputReference
) : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record BorrowTokenDetails(
    [CborIndex(0)]
    Address AdaOwner,

    [CborIndex(1)]
    Address AssetOwner,

    [CborIndex(2)]
    CborBytes PolicyId,

    [CborIndex(3)]
    CborBytes AssetName,

    [CborIndex(4)]
    CborUlong TokenAmount,

    [CborIndex(5)]
    CborUlong LoanAmount,

    [CborIndex(6)]
    CborUlong InterestAmount,

    [CborIndex(7)]
    PosixTime LoanEndTime,

    [CborIndex(8)]
    OutputReference OutputReference
) : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record RepayTokenDetails(
    [CborIndex(0)]
    Address AdaOwner,

    [CborIndex(1)]
    CborUlong TokenAmount,

    [CborIndex(2)]
    CborUlong LoanAmount,

    [CborIndex(3)]
    CborUlong InterestAmount,

    [CborIndex(4)]
    OutputReference OutputReference
) : CborBase;