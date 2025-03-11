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
public partial record NftDatum() : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record LendNftDatum(LendNftDetails LendNftDetails) : NftDatum;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 1)]
public partial record BorrowNftDatum(BorrowNftDetails BorrowNftDetails) : NftDatum;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 2)]
public partial record RepayNftDatum(RepayNftDetails RepayNftDetails) : NftDatum;


[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record LendNftDetails(
    [CborIndex(0)]
    Address AdaOwner,

    [CborIndex(1)]
    CborBytes PolicyId,

    [CborIndex(2)]
    CborUlong LoanAmount,

    [CborIndex(3)]
    CborUlong InterestAmount,

    [CborIndex(4)]
    CborUlong LoanDuration
) : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record BorrowNftDetails(
    [CborIndex(0)]
    Address AdaOwner,

    [CborIndex(1)]
    Address AssetOwner,

    [CborIndex(2)]
    CborBytes PolicyId,

    [CborIndex(3)]
    CborBytes AssetName,

    [CborIndex(4)]
    CborUlong LoanAmount,

    [CborIndex(5)]
    CborUlong InterestAmount,

    [CborIndex(6)]
    PosixTime LoanEndTime,

    [CborIndex(7)]
    OutputReference OutputReference
) : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record RepayNftDetails(
    [CborIndex(0)]
    Address AdaOwner,

    [CborIndex(1)]
    CborUlong LoanAmount,

    [CborIndex(2)]
    CborUlong InterestAmount,

    [CborIndex(3)]
    OutputReference OutputReference
) : CborBase;