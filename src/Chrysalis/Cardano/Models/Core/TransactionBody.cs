using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Map)]
public record TransactionBody(
    [CborProperty("inputs")] CborIndefiniteList<TransactionInput> Inputs,
    [CborProperty("outputs")] CborIndefiniteList<TransactionOutput> Outputs
) : ICbor;
