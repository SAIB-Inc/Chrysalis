using Chrysalis.Cardano.Core.Types.Block.Transaction.Body.ProposalProcedures;
using Chrysalis.Cardano.Core.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Core.Types.Block.Transaction.Body.Certificates;

[CborConverter(typeof(UnionConverter))]
public abstract record Certificate : CborBase;


[CborConverter(typeof(CustomListConverter))]
public record StakeRegistration(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record StakeDeregistration(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record StakeDelegation(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] CborBytes PoolKeyHash
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record PoolRegistration(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes Operator,
    [CborProperty(2)] CborBytes VrfKeyHash,
    [CborProperty(3)] CborUlong Pledge,
    [CborProperty(4)] CborUlong Cost,
    [CborProperty(5)] CborRationalNumber Margin,
    [CborProperty(6)] RewardAccount RewardAccount,
    [CborProperty(7)] CborMaybeIndefList<CborBytes> PoolOwners,
    [CborProperty(8)] CborMaybeIndefList<Relay> Relay,
    [CborProperty(9)] CborNullable<PoolMetadata> PoolMetadata
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record PoolRetirement(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes PoolKeyHash,
    [CborProperty(2)] CborUlong EpochNo
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record RegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] CborUlong Coin
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record UnRegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] CborUlong Coin
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record VoteDelegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] DRep DRep
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record StakeVoteDelegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] CborBytes PoolKeyHash,
    [CborProperty(3)] DRep DRep
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record StakeRegDelegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] CborBytes PoolKeyHash,
    [CborProperty(3)] CborUlong Coin
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record VoteRegDelegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] DRep DRep,
    [CborProperty(3)] CborUlong Coin
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record StakeVoteRegDelegCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential StakeCredential,
    [CborProperty(2)] CborBytes PoolKeyHash,
    [CborProperty(3)] DRep Drep,
    [CborProperty(4)] CborUlong Coin
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record AuthCommitteeHotCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential CommitteeColdCredential,
    [CborProperty(2)] Credential CommitteeHotCredential
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record ResignCommitteeColdCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes CommitteeColdCredential,
    [CborProperty(2)] Anchor? Anchor
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record RegDrepCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential DRepCredential,
    [CborProperty(2)] CborUlong Coin,
    [CborProperty(3)] CborNullable<Anchor> Anchor
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record UnRegDrepCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential DrepCredential,
    [CborProperty(2)] CborUlong Coin
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record UpdateDrepCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] Credential DrepCredential,
    [CborProperty(2)] CborNullable<Anchor> Anchor
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record GenesisKeyDelegation(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] CborBytes GenesisHash,
    [CborProperty(2)] CborBytes GenesisDelegateHash,
    [CborProperty(3)] CborBytes VrfKeyHash
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public record MoveInstantaneousRewardsCert(
    [CborProperty(0)] CborInt Tag,
    [CborProperty(1)] MoveInstantaneousReward MoveInstantaneousReward
) : Certificate;