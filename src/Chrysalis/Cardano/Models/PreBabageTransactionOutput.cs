using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.List)]
public record PreBabbageTransactionOutput(
    [CborProperty("address")] CborBytes Address,
    [CborProperty(0)] CborInt Amount,
    [CborProperty(1)] MultiAsset MultiAsset
) : TransactionOutput;