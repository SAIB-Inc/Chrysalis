
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Cli.Templates.Models;

namespace Chrysalis.Tx.Cli.Templates.Parameters.NftPosition;
public record LendMintParams(
    NftPositionDatum NftPositionDatum,
    TransactionInput MintOutRef,
    string ValidatorAddress,
    string MintPolicy,
    string UserAssetName,
    string ReferenceAssetName
);
    
