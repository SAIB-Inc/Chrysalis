


using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Input;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

namespace Chrysalis.Tx.Models;
public record Utxo(
    [CborIndex(0)]TransactionInput Outref,
    [CborIndex(1)]TransactionOutput Output
);

