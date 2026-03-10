using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;
using Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.V2.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.V2.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.V2.Types.Cardano.Core.Header;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Governance;

[CborSerializable]
[CborUnion]
public partial interface IGovAction : ICborType;

[CborSerializable]
[CborList]
[CborIndex(0)]
public readonly partial record struct ParameterChangeAction : IGovAction
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial GovActionId? ActionId { get; }
    [CborOrder(2)] public partial IProtocolParamUpdate ProtocolParamUpdate { get; }
    [CborOrder(3)] public partial ReadOnlyMemory<byte>? PolicyHash { get; }
}

[CborSerializable]
[CborList]
[CborIndex(1)]
public readonly partial record struct HardForkInitiationAction : IGovAction
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial GovActionId? ActionId { get; }
    [CborOrder(2)] public partial ProtocolVersion ProtocolVersion { get; }
}

[CborSerializable]
[CborList]
[CborIndex(2)]
public readonly partial record struct TreasuryWithdrawalsAction : IGovAction
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Dictionary<RewardAccount, ulong> Withdrawals { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte>? PolicyHash { get; }
}

[CborSerializable]
[CborList]
[CborIndex(3)]
public readonly partial record struct NoConfidence : IGovAction
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial GovActionId? ActionId { get; }
}

[CborSerializable]
[CborList]
[CborIndex(4)]
public readonly partial record struct UpdateCommittee : IGovAction
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial GovActionId? ActionId { get; }
    [CborOrder(2)] public partial ICborMaybeIndefList<Credential> MembersToRemove { get; }
    [CborOrder(3)] public partial MemberTermLimits MembersToAdd { get; }
    [CborOrder(4)] public partial CborRationalNumber Threshold { get; }
}

[CborSerializable]
[CborList]
[CborIndex(5)]
public readonly partial record struct NewConstitution : IGovAction
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial GovActionId? ActionId { get; }
    [CborOrder(2)] public partial Constitution Constitution { get; }
}

[CborSerializable]
[CborList]
[CborIndex(6)]
public readonly partial record struct InfoAction : IGovAction
{
    [CborOrder(0)] public partial int Tag { get; }
}
