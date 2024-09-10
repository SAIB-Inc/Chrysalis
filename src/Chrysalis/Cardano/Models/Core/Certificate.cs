using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(StakeRegistration),
    typeof(StakeDeregistration),
    typeof(StakeDelegation),
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
    [CborProperty(1)] PoolParams PoolParams
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
    [CborProperty(2)] DRep Drep
) : Certificate;

[CborSerializable(CborType.List)]
public record StakeVoteDelegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] CborBytes PoolKeyHash,
    [CborProperty(3)] DRep Drep
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
    [CborProperty(2)] DRep Drep,
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
    [CborProperty(2)] Anchor Anchor
) : Certificate;

[CborSerializable(CborType.List)]
public record RegDrepCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential DrepCredential,
    [CborProperty(2)] CborUlong Coin,
    [CborProperty(3)] Anchor Anchor
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
    [CborProperty(2)] Anchor Anchor
) : Certificate;
