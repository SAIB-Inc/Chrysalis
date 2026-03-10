using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;
using Chrysalis.Codec.V2.Types.Cardano.Core.Certificates;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;

[CborSerializable]
public partial record Withdrawals(Dictionary<RewardAccount, ulong> Value) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}
