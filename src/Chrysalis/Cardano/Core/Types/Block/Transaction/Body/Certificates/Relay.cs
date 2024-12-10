
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Body.Certificates;

[CborConverter(typeof(UnionConverter))]
public abstract record Relay : CborBase;


[CborConverter(typeof(CustomListConverter))]
public record SingleHostAddr(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborNullable<CborUlong> Port,
    [CborProperty(2)] CborNullable<CborBytes> IPv4,
    [CborProperty(3)] CborNullable<CborBytes> IPv6
) : Relay;


[CborConverter(typeof(CustomListConverter))]
public record SingleHostName(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborNullable<CborUlong> Port,
    [CborProperty(2)] CborText DNSName
) : Relay;


[CborConverter(typeof(CustomListConverter))]
public record MultiHostName(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborText DNSName
) : Relay;