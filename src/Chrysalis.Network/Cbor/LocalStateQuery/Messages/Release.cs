using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborList]
[CborIndex(5)]
public partial record Release(
    [CborOrder(0)] int Idx
) : LocalStateQueryMessage;
