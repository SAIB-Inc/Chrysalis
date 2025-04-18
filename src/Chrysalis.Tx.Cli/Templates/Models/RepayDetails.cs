using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Tx.Cli.Templates.Models.Common;

namespace Chrysalis.Tx.Cli.Templates.Models;

[CborSerializable]
[CborConstr(0)]
public partial record RepayDetails(
    [CborOrder(0)]
    LevvyIdentifier Lender,

    [CborOrder(1)]
    LevvyIdentifier Borrower,

    [CborOrder(2)]
    AssetDetails CollateralDetails,

    [CborOrder(3)]
    AssetDetails PrincipalDetails,

    [CborOrder(4)]
    AssetDetails InterestDetails,

    [CborOrder(5)]
    byte[] Tag
) : CborBase;
