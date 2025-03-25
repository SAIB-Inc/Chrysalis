using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;

[CborSerializable]
[CborUnion]
public abstract partial record Certificate : CborBase
{
}

[CborSerializable]
[CborList]
public partial record StakeRegistration(
   [CborOrder(0)] int Tag,
   [CborOrder(1)] Credential StakeCredential
) : Certificate;


[CborSerializable]
[CborList]
public partial record StakeDeregistration(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential
) : Certificate;


[CborSerializable]
[CborList]
public partial record StakeDelegation(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] byte[] PoolKeyHash
) : Certificate;


[CborSerializable]
[CborList]
public partial record PoolRegistration(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] byte[] Operator,
    [CborOrder(2)] byte[] VrfKeyHash,
    [CborOrder(3)] ulong Pledge,
    [CborOrder(4)] ulong Cost,
    [CborOrder(5)] CborRationalNumber Margin,
    [CborOrder(6)] RewardAccount RewardAccount,
    [CborOrder(7)] CborMaybeIndefList<byte[]> PoolOwners,
    [CborOrder(8)] CborMaybeIndefList<Relay> Relay,
    [CborOrder(9)][CborNullable] PoolMetadata? PoolMetadata
) : Certificate;


[CborSerializable]
[CborList]
public partial record PoolRetirement(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] byte[] PoolKeyHash,
    [CborOrder(2)] ulong EpochNo
) : Certificate;


[CborSerializable]
[CborList]
public partial record RegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] ulong Coin
) : Certificate;


[CborSerializable]
[CborList]
public partial record UnRegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] ulong Coin
) : Certificate;


[CborSerializable]
[CborList]
public partial record VoteDelegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] DRep DRep
) : Certificate;


[CborSerializable]
[CborList]
public partial record StakeVoteDelegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] byte[] PoolKeyHash,
    [CborOrder(3)] DRep DRep
) : Certificate;


[CborSerializable]
[CborList]
public partial record StakeRegDelegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] byte[] PoolKeyHash,
    [CborOrder(3)] ulong Coin
) : Certificate;


[CborSerializable]
[CborList]
public partial record VoteRegDelegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] DRep DRep,
    [CborOrder(3)] ulong Coin
) : Certificate;


[CborSerializable]
[CborList]
public partial record StakeVoteRegDelegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] byte[] PoolKeyHash,
    [CborOrder(3)] DRep Drep,
    [CborOrder(4)] ulong Coin
) : Certificate;


[CborSerializable]
[CborList]
public partial record AuthCommitteeHotCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential CommitteeColdCredential,
    [CborOrder(2)] Credential CommitteeHotCredential
) : Certificate;


[CborSerializable]
[CborList]
public partial record ResignCommitteeColdCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] byte[] CommitteeColdCredential,
    [CborOrder(2)] Anchor? Anchor
) : Certificate;


[CborSerializable]
[CborList]
public partial record RegDrepCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential DRepCredential,
    [CborOrder(2)] ulong Coin,
    [CborOrder(3)][CborNullable] Anchor? Anchor
) : Certificate;


[CborSerializable]
[CborList]
public partial record UnRegDrepCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential DrepCredential,
    [CborOrder(2)] ulong Coin
) : Certificate;


[CborSerializable]
[CborList]
public partial record UpdateDrepCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential DrepCredential,
    [CborOrder(2)][CborNullable] Anchor? Anchor
) : Certificate;


[CborSerializable]
[CborList]
public partial record GenesisKeyDelegation(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] byte[] GenesisHash,
    [CborOrder(2)] byte[] GenesisDelegateHash,
    [CborOrder(3)] byte[] VrfKeyHash
) : Certificate;


[CborSerializable]
[CborList]
public partial record MoveInstantaneousRewardsCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] MoveInstantaneousReward MoveInstantaneousReward
) : Certificate;


