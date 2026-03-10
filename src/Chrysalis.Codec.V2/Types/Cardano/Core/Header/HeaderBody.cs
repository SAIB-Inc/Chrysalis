using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Header;

[CborSerializable]
[CborUnion]
public partial interface IBlockHeaderBody : ICborType;

[CborSerializable]
[CborList]
public readonly partial record struct AlonzoHeaderBody : IBlockHeaderBody
{
    [CborOrder(0)] public partial ulong BlockNumber { get; }
    [CborOrder(1)] public partial ulong Slot { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte>? PrevHash { get; }
    [CborOrder(3)] public partial ReadOnlyMemory<byte> IssuerVkey { get; }
    [CborOrder(4)] public partial ReadOnlyMemory<byte> VrfVkey { get; }
    [CborOrder(5)] public partial VrfCert NonceVrf { get; }
    [CborOrder(6)] public partial VrfCert LeaderVrf { get; }
    [CborOrder(7)] public partial ulong BodySize { get; }
    [CborOrder(8)] public partial ReadOnlyMemory<byte> BodyHash { get; }
    [CborOrder(9)] public partial ReadOnlyMemory<byte> OperationalCertHotVkey { get; }
    [CborOrder(10)] public partial ulong OperationalCertSequenceNumber { get; }
    [CborOrder(11)] public partial ulong OperationalCertKesPeriod { get; }
    [CborOrder(12)] public partial ReadOnlyMemory<byte> OperationalCertSigma { get; }
    [CborOrder(13)] public partial ulong ProtocolMajor { get; }
    [CborOrder(14)] public partial ulong ProtocolMinor { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct BabbageHeaderBody : IBlockHeaderBody
{
    [CborOrder(0)] public partial ulong BlockNumber { get; }
    [CborOrder(1)] public partial ulong Slot { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte>? PrevHash { get; }
    [CborOrder(3)] public partial ReadOnlyMemory<byte> IssuerVkey { get; }
    [CborOrder(4)] public partial ReadOnlyMemory<byte> VrfVkey { get; }
    [CborOrder(5)] public partial VrfCert VrfResult { get; }
    [CborOrder(6)] public partial ulong BodySize { get; }
    [CborOrder(7)] public partial ReadOnlyMemory<byte> BodyHash { get; }
    [CborOrder(8)] public partial OperationalCert OperationalCert { get; }
    [CborOrder(9)] public partial ProtocolVersion ProtocolVersion { get; }
}
