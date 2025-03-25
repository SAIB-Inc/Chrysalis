using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Certificates;

[CborSerializable]
[CborUnion]
public abstract partial record Relay : CborBase { }

[CborSerializable]
[CborList]
public partial record SingleHostAddr(
   [CborOrder(0)] int Tag,
   [CborOrder(1)][CborNullable] ulong? Port,
   [CborOrder(2)][CborNullable] byte[]? IPv4,
   [CborOrder(3)][CborNullable] byte[] IPv6
) : Relay;


[CborSerializable]
[CborList]
public partial record SingleHostName(
    [CborOrder(0)] int Tag,
    [CborOrder(1)][CborNullable] ulong? Port,
    [CborOrder(2)] string DNSName
) : Relay;


[CborSerializable]
[CborList]
public partial record MultiHostName(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] string DNSName
) : Relay;