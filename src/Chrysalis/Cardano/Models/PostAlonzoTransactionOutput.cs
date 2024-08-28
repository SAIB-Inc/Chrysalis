using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Map)]
public record PostAlonzoTransactionOutput(
    [CborProperty("address")] CborBytes Address,
    [CborProperty(0)] CborInt Amount
) : TransactionOutput;
