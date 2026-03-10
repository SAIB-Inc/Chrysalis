using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;

namespace Chrysalis.Codec.Types.Cardano.Core.Transaction;

[CborSerializable]
public partial record Withdrawals(Dictionary<RewardAccount, ulong> Value) : ICborType
{
    public ReadOnlyMemory<byte> Raw { get; set; }
    public int ConstrIndex { get; set; }
    public bool IsIndefinite { get; set; }
}
