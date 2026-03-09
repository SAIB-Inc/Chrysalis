using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types;

/// <summary>
/// A CBOR value that wraps pre-encoded CBOR bytes for pass-through serialization.
/// </summary>
/// <param name="Value">The raw CBOR-encoded byte array.</param>
[CborSerializable]
public partial record CborEncodedValue(ReadOnlyMemory<byte> Value) : CborBase;
