using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

/// <summary>
/// Represents a multi-asset collection in a Cardano transaction.
/// </summary>
public abstract partial record MultiAsset : CborBase { }

/// <summary>
/// Represents a multi-asset output mapping policy IDs to their token bundles.
/// </summary>
/// <param name="Value">The dictionary mapping policy ID bytes to token bundle outputs.</param>
[CborSerializable]
public partial record MultiAssetOutput(
    Dictionary<ReadOnlyMemory<byte>, TokenBundleOutput> Value
) : MultiAsset;

/// <summary>
/// Represents a multi-asset mint mapping policy IDs to their minting token bundles.
/// </summary>
/// <param name="Value">The dictionary mapping policy ID bytes to token bundle mints.</param>
[CborSerializable]
public partial record MultiAssetMint(
    Dictionary<ReadOnlyMemory<byte>, TokenBundleMint> Value
) : MultiAsset;
