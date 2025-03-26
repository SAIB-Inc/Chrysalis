using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

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