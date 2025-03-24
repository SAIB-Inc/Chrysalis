using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

[CborSerializable]
[CborList]
public partial record CurrentEraQueryResponse(
    [CborOrder(0)] ulong CurrentEra
) : CborBase;

[CborSerializable]
[CborList]
public partial record UtxoByAddressResponse(
    [CborOrder(0)] Dictionary<TransactionInput, TransactionOutput> Utxos
) : CborBase;

[CborSerializable]
[CborList]
public partial record TransactionInput(
    [CborOrder(0)] byte[] TxHash,
    [CborOrder(1)] ulong Index
) : CborBase;