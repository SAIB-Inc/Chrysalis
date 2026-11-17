using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

/// <summary>
/// Represents the response to a current era query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="CurrentEra">The numeric identifier of the current Cardano era.</param>
[CborSerializable]
public partial record CurrentEraQueryResponse(
    ulong CurrentEra
) : CborRecord;

/// <summary>
/// Represents the response to a UTxO-by-address query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="Utxos">A map of transaction inputs to their corresponding transaction outputs.</param>
[CborSerializable]
[CborList]
public partial record UtxoByAddressResponse(
    [CborOrder(0)] Dictionary<TransactionInput, ITransactionOutput> Utxos
) : CborRecord;

/// <summary>
/// Represents the response to a current protocol parameters query in the LocalStateQuery mini-protocol.
/// </summary>
/// <param name="ProtocolParams">The current protocol parameters of the Cardano network.</param>
[CborSerializable]
[CborList]
public partial record CurrentProtocolParamsResponse(
    [CborOrder(0)] ProtocolParams ProtocolParams
) : CborRecord;

/// <summary>
/// Represents a Cardano era identifier used in LocalStateQuery responses.
/// </summary>
/// <param name="Era">The numeric identifier of the era.</param>
[CborSerializable]
public partial record CurrentEra(ulong Era) : CborRecord;
