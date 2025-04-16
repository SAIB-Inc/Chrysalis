using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Tx.Cli.Templates.Models;

[CborSerializable]
[CborConstr(0)]
public partial record LendDetails(
    [CborOrder(0)]
    LevvyIdentifier Lender,

    [CborOrder(1)]
    AssetDetails PrincipalDetails,

    [CborOrder(2)]
    AssetDetails CollateralDetails,

    [CborOrder(3)]
    AssetDetails InterestDetails,

    [CborOrder(4)]
    PosixTime LoanDuration,

    [CborOrder(5)]
    LevvyType LevvyType

) : CborBase;


[CborSerializable]
[CborConstr(0)]
public partial record AssetDetails(
    [CborOrder(0)]
    byte[] PolicyId,

    [CborOrder(1)]
    byte[] AssetName,

    [CborOrder(2)]
    ulong Amount
) : CborBase;


[CborSerializable]
[CborUnion]
public abstract partial record LevvyType : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record Token : LevvyType;

[CborSerializable]
[CborConstr(1)]
public partial record Nft : LevvyType;


[CborSerializable]
[CborUnion]
public abstract partial record LevvyDatum : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record LendDatum(LendDetails LendDetails) : LevvyDatum;

