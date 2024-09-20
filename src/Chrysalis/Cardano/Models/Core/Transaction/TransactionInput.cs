using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Transaction;

[CborSerializable(CborType.List)]
public record TransactionInput(
    [CborProperty(0)] CborBytes TransactionId,
    [CborProperty(1)] CborInt Index
) : ICbor;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(TransactionInputsWithTag),
    typeof(TransactionInputsList),
])]
public interface TransactionInputs : ICbor;

[CborSerializable(CborType.Tag, Index = 258)]
public record TransactionInputsWithTag(CborDefiniteList<TransactionInput> Value): TransactionInputs;

public record TransactionInputsList(TransactionInput[] Value)
    : CborDefiniteList<TransactionInput>(Value), TransactionInputs;