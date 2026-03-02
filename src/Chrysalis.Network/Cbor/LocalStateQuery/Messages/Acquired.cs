using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record Acquired([CborOrder(0)] int Idx) : LocalStateQueryMessage;
