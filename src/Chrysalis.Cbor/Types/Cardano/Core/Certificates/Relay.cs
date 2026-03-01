using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Certificates;

/// <summary>
/// Represents a stake pool relay for network connectivity.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record Relay : CborBase { }

/// <summary>
/// Represents a relay identified by an optional port and IP addresses.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="Port">The optional port number.</param>
/// <param name="IPv4">The optional IPv4 address bytes.</param>
/// <param name="IPv6">The IPv6 address bytes.</param>
[CborSerializable]
[CborUnionCase(0)]
[CborList]
public partial record SingleHostAddr(
   [CborOrder(0)] int Tag,
   [CborOrder(1)][CborNullable] ulong? Port,
   [CborOrder(2)][CborNullable] ReadOnlyMemory<byte>? IPv4,
   [CborOrder(3)][CborNullable] ReadOnlyMemory<byte>? IPv6
) : Relay;

/// <summary>
/// Represents a relay identified by a DNS name and optional port.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="Port">The optional port number.</param>
/// <param name="DNSName">The DNS hostname of the relay.</param>
[CborSerializable]
[CborUnionCase(1)]
[CborList]
public partial record SingleHostName(
    [CborOrder(0)] int Tag,
    [CborOrder(1)][CborNullable] ulong? Port,
    [CborOrder(2)] string DNSName
) : Relay;

/// <summary>
/// Represents a relay identified by a DNS name that may resolve to multiple hosts.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="DNSName">The DNS hostname of the relay.</param>
[CborSerializable]
[CborUnionCase(2)]
[CborList]
public partial record MultiHostName(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] string DNSName
) : Relay;
