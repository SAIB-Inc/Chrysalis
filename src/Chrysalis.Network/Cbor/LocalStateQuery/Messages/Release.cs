using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborList]
public partial record Release(
    [CborOrder(0)] Value5 Idx
) : LocalStateQueryMessage;
