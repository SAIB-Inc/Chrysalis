using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Common;

/// <summary>
/// Represents a Cardano address as raw bytes containing the network tag and credential hashes.
/// </summary>
/// <param name="Value">The raw address bytes.</param>
[CborSerializable]
public partial record Address(ReadOnlyMemory<byte> Value) : CborBase, ICborPreserveRaw;
