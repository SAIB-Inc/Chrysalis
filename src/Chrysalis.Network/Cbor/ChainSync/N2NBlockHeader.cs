using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Network.Cbor.Common;

namespace Chrysalis.Network.Cbor.ChainSync;

/// <summary>
/// The node-to-node (N2N) <c>MsgRollForward</c> payload: a CBOR array <c>[era, header]</c>. The header
/// shape is era-dependent — Byron (era 0) nests its tag-24 header one level deeper inside a bare array
/// (<c>[[subTag, size], #6.24(header)]</c>), whereas Shelley-and-later (era ≥ 1) carry the tag-24 header
/// directly (<c>#6.24(header)</c>). This is an era-discriminated union (mirroring
/// <see cref="Codec.Types.Cardano.Core.BlockWithEra"/>) so RollForward of <em>any</em> era deserializes
/// without throwing: the lazy reader only records field boundaries, leaving <see cref="ICborType.Raw"/>
/// populated with the full <c>[era, header]</c> bytes. Consumers extract the chain point by routing
/// <see cref="ICborType.Raw"/> through <see cref="ChainSyncHeader.Decode"/>.
/// </summary>
[CborSerializable]
[CborList]
public readonly partial record struct N2NBlockHeader : ICborType
{
    /// <summary>The era identifier (0 = Byron, 1 = Shelley, … 6 = Conway).</summary>
    [CborOrder(0)] public partial int EraTag { get; }

    /// <summary>
    /// The era-shaped header element: the nested Byron container (<see cref="ByronN2NHeader"/>) for era 0,
    /// or the tag-24-wrapped header (<see cref="ShelleyN2NHeader"/>) for Shelley-and-later. Materialization
    /// is era-dispatched, but the wire bytes are always available via <see cref="ICborType.Raw"/>.
    /// </summary>
    [CborOrder(1)]
    [CborUnionHint(nameof(EraTag), 0, typeof(ByronN2NHeader))]
    [CborUnionHint(nameof(EraTag), 1, typeof(ShelleyN2NHeader))]
    [CborUnionHint(nameof(EraTag), 2, typeof(ShelleyN2NHeader))]
    [CborUnionHint(nameof(EraTag), 3, typeof(ShelleyN2NHeader))]
    [CborUnionHint(nameof(EraTag), 4, typeof(ShelleyN2NHeader))]
    [CborUnionHint(nameof(EraTag), 5, typeof(ShelleyN2NHeader))]
    [CborUnionHint(nameof(EraTag), 6, typeof(ShelleyN2NHeader))]
    public partial INetworkBlockHeader Header { get; }
}

/// <summary>
/// Era-shaped N2N header element, discriminated by the leading era tag of <see cref="N2NBlockHeader"/>:
/// <see cref="ByronN2NHeader"/> for era 0, <see cref="ShelleyN2NHeader"/> for Shelley-and-later.
/// </summary>
[CborSerializable]
[CborUnion]
public partial interface INetworkBlockHeader : ICborType;

/// <summary>
/// The Byron (era 0) N2N header body: <c>[[subTag, size], #6.24(header)]</c> — a two-element prefix
/// followed by the tag-24-wrapped header. It exists so the era-0 branch parses without throwing; the
/// header bytes themselves are read era-aware by <see cref="ChainSyncHeader.Decode"/>.
/// </summary>
[CborSerializable]
[CborList]
public partial record ByronN2NHeader(
    [CborOrder(0)] CborDefList<ulong> Prefix,
    [CborOrder(1)] CborEncodedValue Header
) : INetworkBlockHeader
{
    /// <inheritdoc />
    public ReadOnlyMemory<byte> Raw { get; set; }

    /// <inheritdoc />
    public int ConstrIndex { get; set; }

    /// <inheritdoc />
    public bool IsIndefinite { get; set; }
}

/// <summary>
/// The Shelley-and-later (era ≥ 1) N2N header: the tag-24-wrapped block header (<c>#6.24(header)</c>).
/// </summary>
[CborSerializable]
public partial record ShelleyN2NHeader(
    CborEncodedValue Header
) : INetworkBlockHeader
{
    /// <inheritdoc />
    public ReadOnlyMemory<byte> Raw { get; set; }

    /// <inheritdoc />
    public int ConstrIndex { get; set; }

    /// <inheritdoc />
    public bool IsIndefinite { get; set; }
}

/// <summary>
/// Node-to-node (N2N) <c>MsgRollForward</c> — chain-sync index 2, carrying an <c>[era, header]</c>
/// payload (<see cref="N2NBlockHeader"/>). It is the only chain-sync next-response that differs from
/// node-to-client; Await/RollBackward are shared. The <see cref="MessageNextResponse"/> structural
/// probe routes index-2 RollForwards by payload shape: an array selects this N2N member (both Byron
/// and Shelley N2N payloads are arrays), a tag-24 byte string selects <see cref="N2CMessageRollForward"/>.
/// </summary>
[CborSerializable]
[CborList]
[CborIndex(2)]
public partial record N2NMessageRollForward(
    [CborOrder(0)] int Idx,
    [CborOrder(1)] N2NBlockHeader Payload,
    [CborOrder(2)] Tip Tip
) : MessageNextResponse;
