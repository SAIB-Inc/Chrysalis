using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Certificates;

/// <summary>
/// Represents a Cardano reward account as a byte array containing the network tag and credential hash.
/// </summary>
/// <param name="Value">The raw reward account bytes.</param>
[CborSerializable]
public partial record RewardAccount(ReadOnlyMemory<byte> Value) : CborBase;
