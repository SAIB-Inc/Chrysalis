using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types;

[CborSerializable]
[CborTag(30)]
[CborList]
[CborDefinite]
public partial record CborRationalNumber(
    [CborOrder(0)] ulong Numerator,
    [CborOrder(1)] ulong Denominator
) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}
