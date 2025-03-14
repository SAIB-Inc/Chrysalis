using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Attributes;

using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;

// [CborSerializable]
[CborUnion]
public abstract partial record Relay : CborBase<Relay>
{
    // [CborSerializable]
    [CborList]
    public partial record SingleHostAddr(
    [CborIndex(0)] int Tag,
    [CborIndex(1)][CborNullable] ulong? Port,
    [CborIndex(2)][CborNullable] byte[]? IPv4,
    [CborIndex(3)][CborNullable] byte[] IPv6
) : Relay;


    // [CborSerializable]
    [CborList]
    public partial record SingleHostName(
        [CborIndex(0)] int Tag,
        [CborIndex(1)][CborNullable] ulong? Port,
        [CborIndex(2)] string DNSName
    ) : Relay;


    // [CborSerializable]
    [CborList]
    public partial record MultiHostName(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] string DNSName
    ) : Relay;
}