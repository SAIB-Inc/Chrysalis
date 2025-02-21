using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;

[CborConverter(typeof(UnionConverter))]
public abstract record Relay : CborBase;


[CborConverter(typeof(CustomListConverter))]
public record SingleHostAddr(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborNullable<CborUlong> Port,
    [CborIndex(2)] CborNullable<CborBytes> IPv4,
    [CborIndex(3)] CborNullable<CborBytes> IPv6
) : Relay;


[CborConverter(typeof(CustomListConverter))]
public record SingleHostName(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborNullable<CborUlong> Port,
    [CborIndex(2)] CborText DNSName
) : Relay;


[CborConverter(typeof(CustomListConverter))]
public record MultiHostName(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborText DNSName
) : Relay;