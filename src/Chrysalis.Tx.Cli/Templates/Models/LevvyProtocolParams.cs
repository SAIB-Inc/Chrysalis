using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Plutus.Address;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli.Templates.Models;

[CborSerializable]
[CborConstr(0)]
public partial record Subject(
    [CborOrder(0)] byte[] PolicyId,
    [CborOrder(1)] byte[] AssetName
) : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record Ration(
    [CborOrder(0)] byte[] PolicyId,
    [CborOrder(1)] byte[] AssetName
) : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record PoolDetails(
    [CborOrder(0)] Subject PrincipalAsset,
    [CborOrder(1)] Subject CollateralAsset
): CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record LevvyGlobalProtocolParams(
    [CborOrder(0)] GlobalParamsDetails GlobalParamsDetails
): CborBase;

[CborSerializable]
[CborConstr(1)]
public partial record LevvyPoolProtocolParams(
    [CborOrder(0)] PoolParamsDetails PoolParamsDetails
): CborBase;


[CborSerializable]
[CborConstr(0)]
 public partial record GlobalParamsDetails(
    [CborOrder(0)] Rational Fee,
    [CborOrder(1)] Address FeeAddress,
    [CborOrder(2)] MultisigScript Admin,
    [CborOrder(3)] byte[] PoolParamsPolicy
 ) : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record PoolParamsDetails(
    [CborOrder(0)] CborRationalNumber Fee,
    [CborOrder(1)] Address FeeAddress,
    [CborOrder(2)] PoolDetails PoolDetails
) : CborBase;


[CborSerializable]
[CborConstr(0)]
 public partial record Rational(
    [CborOrder(0)] ulong Numerator,
    [CborOrder(1)] ulong Denominator
 ) : CborBase;


//  pub type GlobalParamsDetails {
//   fee: Rational,
//   fee_address: Address,
//   admin: Address,
//   pool_params_policy: PolicyId,
// }     

// // The details of the pool
// // principal_asset: The asset that is being lent
// // collateral_asset: The asset that is being used as collateral
// pub type PoolDetails {
//   principal_asset: Subject,
//   collateral_asset: Subject,
// }

// // The pool protocol parameters for the Levvy protocol
// // fee: The fee that is charged for the pool
// // fee_address: The address that the fee is sent to
// // pool_details: The details of the pool
// pub type PoolParamsDetails {
//   fee: Rational,
//   fee_address: Address,
//   pool_details: PoolDetails,
// }

// pub type Subject {
//   policy_id: PolicyId,
//   asset_name: AssetName,
// }
