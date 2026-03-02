using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

/// <summary>
/// Represents a token bundle mapping asset names to quantities under a single policy.
/// </summary>
public abstract partial record TokenBundle : CborBase { }

/// <summary>
/// Represents a token bundle for transaction outputs mapping asset name bytes to unsigned quantities.
/// </summary>
/// <param name="Value">The dictionary mapping asset name bytes to their unsigned output quantities.</param>
[CborSerializable]
public partial record TokenBundleOutput(Dictionary<ReadOnlyMemory<byte>, ulong> Value) : TokenBundle;

/// <summary>
/// Represents a token bundle for minting operations mapping asset name bytes to signed quantities.
/// </summary>
/// <param name="Value">The dictionary mapping asset name bytes to their signed mint quantities.</param>
[CborSerializable]
public partial record TokenBundleMint(Dictionary<ReadOnlyMemory<byte>, long> Value) : TokenBundle;
