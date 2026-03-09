using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Common;

/// <summary>
/// Represents a CBOR-encoded bounded byte string with a maximum size of 64 bytes.
/// </summary>
/// <param name="Value">The bounded byte array value.</param>
[CborSerializable]
public partial record CborBoundedBytes([CborSize(64)] ReadOnlyMemory<byte> Value) : CborBase;
