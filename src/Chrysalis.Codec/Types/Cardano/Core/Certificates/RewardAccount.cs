using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;

namespace Chrysalis.Codec.Types.Cardano.Core.Certificates;

[CborSerializable]
public partial record RewardAccount(ReadOnlyMemory<byte> Value) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}
