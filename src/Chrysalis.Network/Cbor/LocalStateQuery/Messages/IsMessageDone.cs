using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborList]
[CborIndex(7)]
public partial record IsMessageDone(
    [CborOrder(0)] int Idx
) : LocalStateQueryMessage;
