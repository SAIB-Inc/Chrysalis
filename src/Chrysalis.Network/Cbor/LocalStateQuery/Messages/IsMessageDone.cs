using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborList]
public partial record IsMessageDone(
    [CborOrder(0)] Value7 Idx
) : LocalStateQueryMessage;
