using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Map)]
public record TransactionBody(
    [CborProperty("inputs")] CborList<TransactionInput> Inputs,
    [CborProperty("outputs")] CborList<TransactionOutput> Outputs
);
