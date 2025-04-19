using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Cli.Templates.Models;

namespace Chrysalis.Tx.Cli.Templates.Parameters.NftPosition;

public record BorrowMintParams(
    List<TransactionInput> LockedUtxos,
    NftPositionDatum NftPositionDatum,
    AssetDetails PrincipalDetails,
    AssetDetails CollateralDetails,
    string LockedReferenceAssetName,
    string MintPolicy,
    string ReferenceAssetName,
    string UserAssetName
);