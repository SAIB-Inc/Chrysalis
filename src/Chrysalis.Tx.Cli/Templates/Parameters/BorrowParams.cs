using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Cli.Templates.Models;

namespace Chrysalis.Tx.Cli.Templates.Parameters;

public record BorrowParams(
    List<TransactionInput> LockedUtxos,
    Value PrincipalAmount,
    Value CollateralAmount,
    BorrowDatum BorrowDatum
);