using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(PostAlonzoTransactionOutput), typeof(PreBabbageTransactionOutput)])]
public record TransactionOutput : ICbor;