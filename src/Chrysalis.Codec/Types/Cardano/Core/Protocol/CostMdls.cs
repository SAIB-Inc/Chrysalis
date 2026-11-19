using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborMap]
public partial record CostMdls(
    [CborProperty(0)] CborList<long>? PlutusV1,
    [CborProperty(1)] CborList<long>? PlutusV2,
    [CborProperty(2)] CborList<long>? PlutusV3
) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}
