using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

/// <summary>
/// Represents a Cardano address as raw bytes containing the network tag and credential hashes.
/// </summary>
/// <param name="Value">The raw address bytes.</param>
[CborSerializable]
public partial record Address(byte[] Value) : CborBase, ICborPreserveRaw;
