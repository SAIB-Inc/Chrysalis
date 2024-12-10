using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(SingleHostAddr),
    typeof(SingleHostName),
    typeof(MultiHostName),
])]
public record Relay : RawCbor;

[CborSerializable(CborType.List)]
public record SingleHostAddr(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborNullable<CborUlong> Port,
    [CborProperty(2)] CborNullable<CborBytes> IPv4, 
    [CborProperty(3)] CborNullable<CborBytes> IPv6   
) : Relay;

[CborSerializable(CborType.List)]
public record SingleHostName(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborNullable<CborUlong> Port,
    [CborProperty(2)] CborText DNSName
) : Relay;

[CborSerializable(CborType.List)]
public record MultiHostName(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborText DNSName
) : Relay;