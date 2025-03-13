using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;

[CborSerializable]
[CborUnion]
public abstract partial record Certificate : CborBase<Certificate>;


[CborSerializable]
[CborList]
public partial record StakeRegistration(
    [CborIndex(0)] int Tag,
    [CborIndex(1)] Credential StakeCredential
) : Certificate;


[CborSerializable]
[CborList]
public partial record StakeDeregistration(
    [CborIndex(0)] int Tag,
    [CborIndex(1)] Credential StakeCredential
) : Certificate;


[CborSerializable]
[CborList]
public partial record StakeDelegation(
    [CborIndex(0)] int Tag,
    [CborIndex(1)] Credential StakeCredential,
    [CborIndex(2)] byte[] PoolKeyHash
) : Certificate;


[CborSerializable]
[CborList]
public partial record PoolRegistration(
    [CborIndex(0)] int Tag,
    [CborIndex(1)] byte[] Operator,
    [CborIndex(2)] byte[] VrfKeyHash,
    [CborIndex(3)] ulong Pledge,
    [CborIndex(4)] ulong Cost,
    [CborIndex(5)] CborRationalNumber Margin,
    [CborIndex(6)] RewardAccount RewardAccount,
    [CborIndex(7)] CborMaybeIndefList<byte[]> PoolOwners,
    [CborIndex(8)] CborMaybeIndefList<Relay> Relay,
    [CborIndex(9)] CborNullable<PoolMetadata> PoolMetadata
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record PoolRetirement(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborBytes PoolKeyHash,
    [CborIndex(2)] CborUlong EpochNo
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record RegCert(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] Credential StakeCredential,
    [CborIndex(2)] CborUlong Coin
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record UnRegCert(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] Credential StakeCredential,
    [CborIndex(2)] CborUlong Coin
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record VoteDelegCert(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] Credential StakeCredential,
    [CborIndex(2)] DRep DRep
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record StakeVoteDelegCert(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] Credential StakeCredential,
    [CborIndex(2)] CborBytes PoolKeyHash,
    [CborIndex(3)] DRep DRep
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record StakeRegDelegCert(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] Credential StakeCredential,
    [CborIndex(2)] CborBytes PoolKeyHash,
    [CborIndex(3)] CborUlong Coin
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record VoteRegDelegCert(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] Credential StakeCredential,
    [CborIndex(2)] DRep DRep,
    [CborIndex(3)] CborUlong Coin
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record StakeVoteRegDelegCert(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] Credential StakeCredential,
    [CborIndex(2)] CborBytes PoolKeyHash,
    [CborIndex(3)] DRep Drep,
    [CborIndex(4)] CborUlong Coin
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record AuthCommitteeHotCert(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] Credential CommitteeColdCredential,
    [CborIndex(2)] Credential CommitteeHotCredential
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record ResignCommitteeColdCert(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborBytes CommitteeColdCredential,
    [CborIndex(2)] Anchor? Anchor
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record RegDrepCert(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] Credential DRepCredential,
    [CborIndex(2)] CborUlong Coin,
    [CborIndex(3)] CborNullable<Anchor> Anchor
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record UnRegDrepCert(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] Credential DrepCredential,
    [CborIndex(2)] CborUlong Coin
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record UpdateDrepCert(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] Credential DrepCredential,
    [CborIndex(2)] CborNullable<Anchor> Anchor
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record GenesisKeyDelegation(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] CborBytes GenesisHash,
    [CborIndex(2)] CborBytes GenesisDelegateHash,
    [CborIndex(3)] CborBytes VrfKeyHash
) : Certificate;


[CborConverter(typeof(CustomListConverter))]
public partial record MoveInstantaneousRewardsCert(
    [CborIndex(0)] CborInt Tag,
    [CborIndex(1)] MoveInstantaneousReward MoveInstantaneousReward
) : Certificate;