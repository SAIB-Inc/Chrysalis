using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Certificates;

/// <summary>
/// Represents stake pool metadata containing a URL and its content hash.
/// </summary>
/// <param name="Url">The URL pointing to the pool metadata JSON.</param>
/// <param name="PoolMetadataHash">The hash of the metadata content for verification.</param>
[CborSerializable]
[CborList]
public partial record PoolMetadata(
    [CborOrder(0)] string Url,
    [CborOrder(1)] ReadOnlyMemory<byte> PoolMetadataHash
) : CborBase;
