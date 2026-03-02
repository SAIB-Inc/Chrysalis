using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record Failure(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] FailureReason Reason
) : LocalStateQueryMessage;

[CborSerializable]
[CborUnion]
public abstract partial record FailureReason : CborBase;

[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record AcquireFailurePointTooOld(
    [CborOrder(0)] int Idx
) : FailureReason;

[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record AcquireFailurePointNotOnChain(
    [CborOrder(0)] int Idx
) : FailureReason;
