using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;

namespace Chrysalis.Codec.Types.Cardano.Core.Transaction;

[CborSerializable]
public readonly partial record struct Withdrawals : ICborType
{
    public partial CborMap<RewardAccount, ulong> Value { get; }
}
