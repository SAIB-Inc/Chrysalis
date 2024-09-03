using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Map)]
public record TransactionBody(
    [CborProperty("inputs")] CborIndefiniteList<TransactionInput> Inputs,
    [CborProperty("outputs")] CborIndefiniteList<TransactionOutput> Outputs
);
