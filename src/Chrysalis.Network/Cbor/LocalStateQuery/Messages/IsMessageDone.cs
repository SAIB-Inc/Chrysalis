using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

/// <summary>
/// Represents the Done message in the Ouroboros LocalStateQuery mini-protocol, signaling that the client is terminating the protocol session.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(7)]
public partial record IsMessageDone(
    [CborOrder(0)] int Idx
) : LocalStateQueryMessage;
