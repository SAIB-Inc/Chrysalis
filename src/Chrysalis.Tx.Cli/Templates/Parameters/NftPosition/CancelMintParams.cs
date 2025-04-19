using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Tx.Cli.Templates.Parameters.NftPosition;

public record CancelMintParams(
    List<TransactionInput> lockedUtxos,
    Value principalAmount,
    string MintPolicy,
    string ReferenceAssetName,
    string UserAssetName
);