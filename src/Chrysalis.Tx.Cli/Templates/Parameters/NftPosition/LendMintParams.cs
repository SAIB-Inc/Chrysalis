using Chrysalis.Tx.Cli.Templates.Models;

namespace Chrysalis.Tx.Cli.Templates.Parameters.NftPosition;
public record LendMintParams(
    NftPositionDatum NftPositionDatum,
    AssetDetails PrincipalDetails,
    string ValidatorAddress,
    string MintPolicy,
    string UserAssetName,
    string ReferenceAssetName
);
    
