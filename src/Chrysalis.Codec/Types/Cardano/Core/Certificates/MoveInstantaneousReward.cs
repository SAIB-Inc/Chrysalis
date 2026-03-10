using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.Types.Cardano.Core.Certificates;

[CborSerializable]
[CborList]
public readonly partial record struct MoveInstantaneousReward : ICborType
{
    [CborOrder(0)] public partial int Source { get; }
    [CborOrder(1)] public partial ITarget Target { get; }
}

[CborSerializable]
[CborUnion]
public partial interface ITarget : ICborType;

[CborSerializable]
public readonly partial record struct StakeCredentials : ITarget
{
    public partial Dictionary<Credential, long> Value { get; }
}

[CborSerializable]
public readonly partial record struct OtherAccountingPot : ITarget
{
    public partial ulong Amount { get; }
}
