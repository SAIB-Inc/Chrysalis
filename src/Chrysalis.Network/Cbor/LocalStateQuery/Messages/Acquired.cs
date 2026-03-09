using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

/// <summary>
/// Represents the Acquired message in the Ouroboros LocalStateQuery mini-protocol, confirming that the ledger state has been successfully acquired.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record Acquired([CborOrder(0)] int Idx) : LocalStateQueryMessage;
