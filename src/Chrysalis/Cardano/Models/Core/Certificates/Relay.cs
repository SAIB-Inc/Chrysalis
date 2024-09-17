using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Plutus;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Certificates;

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
    [CborProperty(1)] Option<CborUlong> Port,
    [CborProperty(2)] Option<CborBytes> IPv4, 
    [CborProperty(3)] Option<CborBytes> IPv6   
) : Relay;

[CborSerializable(CborType.List)]
public record SingleHostName(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Option<CborUlong> Port,
    [CborProperty(2)] CborText DNSName
) : Relay;

public record MultiHostName(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborText DNSName
) : Relay;