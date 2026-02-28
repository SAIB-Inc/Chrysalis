using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types;

/// <summary>
/// A CBOR value that wraps pre-encoded CBOR bytes for pass-through serialization.
/// </summary>
/// <param name="Value">The raw CBOR-encoded byte array.</param>
[CborSerializable]
public partial record CborEncodedValue(ReadOnlyMemory<byte> Value) : CborBase;
