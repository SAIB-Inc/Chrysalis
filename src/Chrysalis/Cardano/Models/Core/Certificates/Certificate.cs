using Chrysalis.Cbor;
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cardano.Models.Core.Governance;

namespace Chrysalis.Cardano.Models.Core.Certificates;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(StakeRegistration),
    typeof(StakeDeregistration),
    typeof(StakeDelegation),
    typeof(PoolRegistration),
    typeof(PoolRetirement),
    typeof(RegCert),
    typeof(UnRegCert),
    typeof(VoteDelegCert),
    typeof(StakeVoteDelegCert),
    typeof(StakeRegDelegCert),
    typeof(VoteRegDelegCert),
    typeof(StakeVoteRegDelegCert),
    typeof(AuthCommitteeHotCert),
    typeof(ResignCommitteeColdCert),
    typeof(RegDrepCert),
    typeof(UnRegDrepCert),
    typeof(UpdateDrepCert)
])]
public record Certificate : ICbor;

[CborSerializable(CborType.List)]
public record StakeRegistration(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential
) : Certificate;

[CborSerializable(CborType.List)]
public record StakeDeregistration(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential
) : Certificate;

[CborSerializable(CborType.List)]
public record StakeDelegation(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] CborBytes PoolKeyHash
) : Certificate;

[CborSerializable(CborType.List)]
public record PoolRegistration(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes Operator,
    [CborProperty(2)] CborBytes VrfKeyHash,
    [CborProperty(3)] CborUlong Pledge,
    [CborProperty(4)] CborUlong Cost,
    [CborProperty(5)] CborRationalNumber Margin,
    [CborProperty(6)] RewardAccount RewardAccount,
    [CborProperty(7)] CborDefiniteList<CborBytes> PoolOwners,
    [CborProperty(8)] CborDefiniteList<Relay> Relay,
    [CborProperty(9)] CborNullable<PoolMetadata> PoolMetadata
) : Certificate;

[CborSerializable(CborType.List)]
public record PoolRetirement(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes PoolKeyHash,
    [CborProperty(2)] CborUlong EpochNo
) : Certificate;

[CborSerializable(CborType.List)]
public record RegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] CborUlong Coin
) : Certificate;

[CborSerializable(CborType.List)]
public record UnRegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] CborUlong Coin
) : Certificate;

[CborSerializable(CborType.List)]
public record VoteDelegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] DRep DRep
) : Certificate;

[CborSerializable(CborType.List)]
public record StakeVoteDelegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] CborBytes PoolKeyHash,
    [CborProperty(3)] DRep DRep
) : Certificate;

[CborSerializable(CborType.List)]
public record StakeRegDelegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] CborBytes PoolKeyHash,
    [CborProperty(3)] CborUlong Coin
) : Certificate;

[CborSerializable(CborType.List)]
public record VoteRegDelegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] DRep DRep,
    [CborProperty(3)] CborUlong Coin
) : Certificate;

[CborSerializable(CborType.List)]
public record StakeVoteRegDelegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] CborBytes PoolKeyHash,
    [CborProperty(3)] DRep Drep,
    [CborProperty(4)] CborUlong Coin
) : Certificate;

[CborSerializable(CborType.List)]
public record AuthCommitteeHotCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential CommitteeColdCredential,
    [CborProperty(2)] Credential CommitteeHotCredential
) : Certificate;

[CborSerializable(CborType.List)]
public record ResignCommitteeColdCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes CommitteeColdCredential,
    [CborProperty(2)] Anchor? Anchor
) : Certificate;

[CborSerializable(CborType.List)]
public record RegDrepCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential DRepCredential,
    [CborProperty(2)] CborUlong Coin,
    [CborProperty(3)] CborNullable<Anchor> Anchor
) : Certificate;

[CborSerializable(CborType.List)]
public record UnRegDrepCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential DrepCredential,
    [CborProperty(2)] CborUlong Coin
) : Certificate;

[CborSerializable(CborType.List)]
public record UpdateDrepCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential DrepCredential,
    [CborProperty(2)] CborNullable<Anchor> Anchor
) : Certificate;

[CborSerializable(CborType.List)]
public record GenesisKeyDelegation(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes GenesisHash,
    [CborProperty(2)] CborBytes GenesisDelegateHash,
    [CborProperty(3)] CborBytes VrfKeyHash
) : Certificate;

[CborSerializable(CborType.List)]
public record MoveInstantaneousRewardsCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] MoveInstantaneousReward MoveInstantaneousReward
) : Certificate;