using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.Governance;

namespace Chrysalis.Codec.Types.Cardano.Core.Certificates;

[CborSerializable]
[CborUnion]
public partial interface ICertificate : ICborType;

[CborSerializable]
[CborList]
[CborIndex(0)]
public readonly partial record struct StakeRegistration : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential StakeCredential { get; }
}

[CborSerializable]
[CborList]
[CborIndex(1)]
public readonly partial record struct StakeDeregistration : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential StakeCredential { get; }
}

[CborSerializable]
[CborList]
[CborIndex(2)]
public readonly partial record struct StakeDelegation : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential StakeCredential { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte> PoolKeyHash { get; }
}

[CborSerializable]
[CborList]
[CborIndex(3)]
public readonly partial record struct PoolRegistration : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> Operator { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte> VrfKeyHash { get; }
    [CborOrder(3)] public partial ulong Pledge { get; }
    [CborOrder(4)] public partial ulong Cost { get; }
    [CborOrder(5)] public partial CborRationalNumber Margin { get; }
    [CborOrder(6)] public partial RewardAccount RewardAccount { get; }
    [CborOrder(7)] public partial ICborMaybeIndefList<ReadOnlyMemory<byte>> PoolOwners { get; }
    [CborOrder(8)] public partial ICborMaybeIndefList<IRelay> Relays { get; }
    [CborOrder(9)] public partial PoolMetadata? PoolMetadata { get; }
}

[CborSerializable]
[CborList]
[CborIndex(4)]
public readonly partial record struct PoolRetirement : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> PoolKeyHash { get; }
    [CborOrder(2)] public partial ulong Epoch { get; }
}

[CborSerializable]
[CborList]
[CborIndex(5)]
public readonly partial record struct GenesisKeyDelegation : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte> GenesisHash { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte> GenesisDelegateHash { get; }
    [CborOrder(3)] public partial ReadOnlyMemory<byte> VrfKeyHash { get; }
}

[CborSerializable]
[CborList]
[CborIndex(6)]
public readonly partial record struct MoveInstantaneousRewardsCert : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial MoveInstantaneousReward MoveInstantaneousReward { get; }
}

[CborSerializable]
[CborList]
[CborIndex(7)]
public readonly partial record struct RegCert : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential StakeCredential { get; }
    [CborOrder(2)] public partial ulong Coin { get; }
}

[CborSerializable]
[CborList]
[CborIndex(8)]
public readonly partial record struct UnRegCert : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential StakeCredential { get; }
    [CborOrder(2)] public partial ulong Coin { get; }
}

[CborSerializable]
[CborList]
[CborIndex(9)]
public readonly partial record struct VoteDelegCert : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential StakeCredential { get; }
    [CborOrder(2)] public partial IDRep DRep { get; }
}

[CborSerializable]
[CborList]
[CborIndex(10)]
public readonly partial record struct StakeVoteDelegCert : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential StakeCredential { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte> PoolKeyHash { get; }
    [CborOrder(3)] public partial IDRep DRep { get; }
}

[CborSerializable]
[CborList]
[CborIndex(11)]
public readonly partial record struct StakeRegDelegCert : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential StakeCredential { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte> PoolKeyHash { get; }
    [CborOrder(3)] public partial ulong Coin { get; }
}

[CborSerializable]
[CborList]
[CborIndex(12)]
public readonly partial record struct VoteRegDelegCert : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential StakeCredential { get; }
    [CborOrder(2)] public partial IDRep DRep { get; }
    [CborOrder(3)] public partial ulong Coin { get; }
}

[CborSerializable]
[CborList]
[CborIndex(13)]
public readonly partial record struct StakeVoteRegDelegCert : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential StakeCredential { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte> PoolKeyHash { get; }
    [CborOrder(3)] public partial IDRep DRep { get; }
    [CborOrder(4)] public partial ulong Coin { get; }
}

[CborSerializable]
[CborList]
[CborIndex(14)]
public readonly partial record struct AuthCommitteeHotCert : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential ColdCredential { get; }
    [CborOrder(2)] public partial Credential HotCredential { get; }
}

[CborSerializable]
[CborList]
[CborIndex(15)]
public readonly partial record struct ResignCommitteeColdCert : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential ColdCredential { get; }
    [CborOrder(2)] public partial Anchor? Anchor { get; }
}

[CborSerializable]
[CborList]
[CborIndex(16)]
public readonly partial record struct RegDrepCert : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential DRepCredential { get; }
    [CborOrder(2)] public partial ulong Coin { get; }
    [CborOrder(3)] public partial Anchor? Anchor { get; }
}

[CborSerializable]
[CborList]
[CborIndex(17)]
public readonly partial record struct UnRegDrepCert : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential DRepCredential { get; }
    [CborOrder(2)] public partial ulong Coin { get; }
}

[CborSerializable]
[CborList]
[CborIndex(18)]
public readonly partial record struct UpdateDrepCert : ICertificate
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial Credential DRepCredential { get; }
    [CborOrder(2)] public partial Anchor? Anchor { get; }
}
