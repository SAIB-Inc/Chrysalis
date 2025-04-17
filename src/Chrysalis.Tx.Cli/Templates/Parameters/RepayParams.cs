using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Cli.Templates.Models;

namespace Chrysalis.Tx.Cli.Templates.Parameters;

public record RepayParams(
    List<TransactionInput> LockedUtxos,
    RepayDatum RepayDatum
);