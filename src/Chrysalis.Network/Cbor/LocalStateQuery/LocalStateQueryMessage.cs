using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

/// <summary>
/// Base CBOR union type for all messages in the Ouroboros LocalStateQuery mini-protocol.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record LocalStateQueryMessage : CborBase;
