using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

/// <summary>
/// Represents a CBOR-encoded bounded byte string with a maximum size of 64 bytes.
/// </summary>
/// <param name="Value">The bounded byte array value.</param>
[CborSerializable]
public partial record CborBoundedBytes([CborSize(64)] byte[] Value) : CborBase;
