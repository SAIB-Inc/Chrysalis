using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborList]
[CborIndex(4)]
public partial record Result(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] CborEncodedValue QueryResult
) : LocalStateQueryMessage;
