using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using SAIB.Cbor;
using SAIB.Cbor.Serialization;

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
public abstract partial record FailureReason : CborRecord
{
    private const string ExpectedReason = "acquire failure reason (0 or 1)";

    /// <summary>Reads a <see cref="FailureReason"/> from a bare unsigned CBOR integer (0 or 1) and reports the number of bytes consumed.</summary>
    public static FailureReason Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        if (data.IsEmpty || (data.Span[0] >> 5) != 0)
        {
            throw CborError.Decode(data, 0, nameof(FailureReason), ExpectedReason);
        }

        CborReader reader = new(data.Span);
        ulong code;
        try
        {
            code = reader.ReadUInt64();
        }
        catch (CborException error)
        {
            throw CborError.Wrap(error, data, nameof(FailureReason), ExpectedReason);
        }

        return code switch
        {
            0 => AcquireFailurePointTooOld.Read(data, out bytesConsumed),
            1 => AcquireFailurePointNotOnChain.Read(data, out bytesConsumed),
            _ => throw CborError.Decode(data, 0, nameof(FailureReason), ExpectedReason, code)
        };
    }
}

/// <summary>
/// Indicates that the requested point is too old and has been pruned from the volatile chain.
/// </summary>
/// <param name="Idx">The acquire failure reason wire code (0).</param>
[CborSerializable]
public partial record AcquireFailurePointTooOld(int Idx) : FailureReason;

/// <summary>
/// Indicates that the requested point does not exist on the current chain.
/// </summary>
/// <param name="Idx">The acquire failure reason wire code (1).</param>
[CborSerializable]
public partial record AcquireFailurePointNotOnChain(int Idx) : FailureReason;

internal readonly record struct AcquireFailurePointTooOldValidator : ICborValidator<AcquireFailurePointTooOld>
{
    public bool Validate(AcquireFailurePointTooOld input) => input.Idx == 0;
}

internal readonly record struct AcquireFailurePointNotOnChainValidator : ICborValidator<AcquireFailurePointNotOnChain>
{
    public bool Validate(AcquireFailurePointNotOnChain input) => input.Idx == 1;
}
