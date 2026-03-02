using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

/// <summary>
/// Represents the Result message in the Ouroboros LocalStateQuery mini-protocol, containing the CBOR-encoded response to a ledger state query.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
/// <param name="QueryResult">The CBOR-encoded query result that can be deserialized into the appropriate response type.</param>
[CborSerializable]
[CborList]
[CborIndex(4)]
public partial record Result(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] CborEncodedValue QueryResult
) : LocalStateQueryMessage;
