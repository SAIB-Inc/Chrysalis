using System.Runtime.CompilerServices;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.Types.Cardano.Core.Certificates;

[CborSerializable]
[CborList]
public readonly partial record struct MoveInstantaneousReward : ICborType
{
    [CborOrder(0)] public partial int Source { get; }
    [CborOrder(1)] public partial Target Target { get; }
}

[CborSerializable]
[CborUnion(typeof(StakeCredentials), typeof(OtherAccountingPot))]
[Union]
public readonly partial struct Target : ICborType;

[CborSerializable]
public readonly partial record struct StakeCredentials : ICborType
{
    public partial Dictionary<Credential, long> Value { get; }
}

[CborSerializable]
public readonly partial record struct OtherAccountingPot : ICborType
{
    public partial ulong Amount { get; }
}
