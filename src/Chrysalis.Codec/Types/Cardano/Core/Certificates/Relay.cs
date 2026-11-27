using System.Runtime.CompilerServices;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Certificates;

[CborSerializable]
[CborUnion(typeof(SingleHostAddr), typeof(SingleHostName), typeof(MultiHostName))]
[Union]
public readonly partial struct Relay : ICborType;

[CborSerializable]
[CborList]
[CborIndex(0)]
public readonly partial record struct SingleHostAddr : ICborType
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial int? Port { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte>? Ipv4 { get; }
    [CborOrder(3)] public partial ReadOnlyMemory<byte>? Ipv6 { get; }
}

[CborSerializable]
[CborList]
[CborIndex(1)]
public readonly partial record struct SingleHostName : ICborType
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial int? Port { get; }
    [CborOrder(2), CborLedgerText] public partial string DnsName { get; }
}

[CborSerializable]
[CborList]
[CborIndex(2)]
public readonly partial record struct MultiHostName : ICborType
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1), CborLedgerText] public partial string DnsName { get; }
}
