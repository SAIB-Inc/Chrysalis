using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Cli.Templates.Models;

namespace Chrysalis.Tx.Cli.Templates.Parameters.NftPosition;

public record ClaimMintParams(
    List<TransactionInput> LockedUtxos,
    TransactionInput UserNftOutRef,
    AssetDetails CollateralDetails,
    AssetDetails InterestDetails,
    string MintPolicy,
    string LockedReferenceAssetName,
    string UserAssetName,
    string DatumTag
);