using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Cli.Templates.Models;

namespace Chrysalis.Tx.Cli.Templates.Parameters.NftPosition;

public record RepayMintParams(
    List<TransactionInput> LockedUtxos,
    NftPositionDatum NftPositionDatum,
    AssetDetails CollateralDetails,
    AssetDetails InterestDetails,
    TransactionInput UserNftOutRef,
    string LockedReferenceAssetName,
    string MintPolicy,
    string UserAssetName
);