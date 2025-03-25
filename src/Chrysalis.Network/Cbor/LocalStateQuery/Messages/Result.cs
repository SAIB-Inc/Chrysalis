using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborList]
public partial record Result(
    [CborOrder(0)] Value4 Idx,
    [CborOrder(1)] CborEncodedValue QueryResult
) : LocalStateQueryMessage;

