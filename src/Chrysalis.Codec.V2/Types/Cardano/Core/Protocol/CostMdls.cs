using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Protocol;

[CborSerializable]
[CborMap]
public partial record CostMdls(
    [CborProperty(0)] ICborMaybeIndefList<long>? PlutusV1,
    [CborProperty(1)] ICborMaybeIndefList<long>? PlutusV2,
    [CborProperty(2)] ICborMaybeIndefList<long>? PlutusV3
) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}
