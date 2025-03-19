using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;

[CborSerializable]
[CborUnion]
public abstract partial record Relay : CborBase<Relay>
{
}

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