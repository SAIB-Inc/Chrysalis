using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
namespace Chrysalis.Tx.Cli.Templates.Parameters;

public record ClaimParams(List<TransactionInput> LockedUtxos);