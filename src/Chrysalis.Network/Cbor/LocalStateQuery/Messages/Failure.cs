using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborList]
public partial record Failure(
    [CborOrder(0)] Value2 Idx,
    [CborOrder(1)] FailureReason Reason
) : LocalStateQueryMessage;

[CborSerializable]
[CborUnion]
public abstract partial record FailureReason : CborBase;

[CborSerializable]
public partial record AcquireFailurePointTooOld(Value0 Value) : FailureReason;

[CborSerializable]
public partial record AcquireFailurePointNotOnChain(Value1 Value) : FailureReason;