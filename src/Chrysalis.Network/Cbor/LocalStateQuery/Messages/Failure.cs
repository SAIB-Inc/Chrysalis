using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;

namespace Chrysalis.Network.Cbor.LocalStateQuery.Messages;

/// <summary>
/// Represents the Failure message in the Ouroboros LocalStateQuery mini-protocol, indicating that an acquire or re-acquire request failed.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
/// <param name="Reason">The reason the acquire request failed.</param>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record Failure(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] FailureReason Reason
) : LocalStateQueryMessage;

/// <summary>
/// Base CBOR union type for failure reasons in the LocalStateQuery mini-protocol.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record FailureReason : CborBase;

/// <summary>
/// Indicates that the requested point is too old and has been pruned from the volatile chain.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(0)]
public partial record AcquireFailurePointTooOld(
    [CborOrder(0)] int Idx
) : FailureReason;

/// <summary>
/// Indicates that the requested point does not exist on the current chain.
/// </summary>
/// <param name="Idx">The CBOR message type index.</param>
[CborSerializable]
[CborList]
[CborIndex(1)]
public partial record AcquireFailurePointNotOnChain(
    [CborOrder(0)] int Idx
) : FailureReason;
