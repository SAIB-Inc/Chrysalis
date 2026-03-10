using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Byron;

[CborSerializable]
[CborList]
public readonly partial record struct ByronDlg : ICborType
{
    [CborOrder(0)] public partial ulong Epoch { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> Issuer { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte> Delegate { get; }
    [CborOrder(3)] public partial ReadOnlyMemory<byte> Certificate { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct ByronBlockSig : ICborType
{
    [CborOrder(0)] public partial int Variant { get; }
    [CborOrder(1)] public partial CborEncodedValue Data { get; }
}
