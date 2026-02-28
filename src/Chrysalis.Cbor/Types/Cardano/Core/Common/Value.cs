using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

/// <summary>
/// Represents a Cardano transaction output value, either as pure lovelace or lovelace with multi-assets.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record Value : CborBase
{
}

/// <summary>
/// Represents a pure lovelace value without any multi-assets.
/// </summary>
/// <param name="Value">The amount in lovelace.</param>
[CborSerializable]
public partial record Lovelace(ulong Value) : Value;

/// <summary>
/// Represents a lovelace value combined with multi-asset tokens.
/// </summary>
/// <param name="LovelaceValue">The lovelace amount.</param>
/// <param name="MultiAsset">The multi-asset token bundle.</param>
[CborSerializable]
[CborList]
public partial record LovelaceWithMultiAsset(
     [CborOrder(0)] Lovelace LovelaceValue,
     [CborOrder(1)] MultiAssetOutput MultiAsset
 ) : Value;
