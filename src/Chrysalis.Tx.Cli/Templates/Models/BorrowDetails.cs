using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Tx.Cli.Templates.Models;

[CborSerializable]
[CborConstr(0)]
public partial record BorrowDetails(
    [CborOrder(0)]
    LevvyIdentifier Lender,

    [CborOrder(1)]
    LevvyIdentifier Borrower,

    [CborOrder(2)]
    AssetDetails PrincipalDetails,

    [CborOrder(3)]
    AssetDetails CollateralDetails,

    [CborOrder(4)]
    AssetDetails InterestDetails,

    [CborOrder(5)]
    PosixTime LoanEndTime,

    [CborOrder(6)]
    LevvyType LevvyType,

    [CborOrder(7)]
    byte[] Tag
) : CborBase;

[CborSerializable]
[CborConstr(1)]
public partial record BorrowDatum(BorrowDetails BorrowDetails) : LevvyDatum;
