using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(SingleHostAddr),
    typeof(SingleHostName),
    typeof(MultiHostName),
])]
public record Relay : ICbor;

[CborSerializable(CborType.List)]
public record SingleHostAddr(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborUlong? Port,
    [CborProperty(2)] CborBytes? IPv4, 
    [CborProperty(3)] CborBytes? IPv6   
) : Relay;

[CborSerializable(CborType.List)]
public record SingleHostName(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborUlong? Port,
    [CborProperty(2)] CborText DNSName
) : Relay;

public record MultiHostName(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborText DNSName
) : Relay;