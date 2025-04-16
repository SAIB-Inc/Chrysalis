using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Tx.Cli.Templates.Parameters;

public record CancelParams(List<TransactionInput> lockedUtxos, Value principalAmount);