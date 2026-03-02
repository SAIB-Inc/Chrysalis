using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

/// <summary>
/// Represents the Release message in the Ouroboros LocalStateQuery mini-protocol, releasing the previously acquired ledger state.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(5)]
public partial record Release(
    [CborOrder(0)] int Idx
) : LocalStateQueryMessage;
