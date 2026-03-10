using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Certificates;

[CborSerializable]
[CborUnion]
public partial interface IRelay : ICborType;

[CborSerializable]
[CborList]
[CborIndex(0)]
public readonly partial record struct SingleHostAddr : IRelay
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial int? Port { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte>? Ipv4 { get; }
    [CborOrder(3)] public partial ReadOnlyMemory<byte>? Ipv6 { get; }
}

[CborSerializable]
[CborList]
[CborIndex(1)]
public readonly partial record struct SingleHostName : IRelay
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial int? Port { get; }
    [CborOrder(2)] public partial string DnsName { get; }
}

[CborSerializable]
[CborList]
[CborIndex(2)]
public readonly partial record struct MultiHostName : IRelay
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial string DnsName { get; }
}
