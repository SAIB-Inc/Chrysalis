using Chrysalis.Cbor.Attributes;

using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.ProposalProcedures;
using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Body.Certificates;

// [CborSerializable]
[CborUnion]
public abstract partial record Certificate : CborBase<Certificate>
{
    // [CborSerializable]
    [CborList]
    public partial record StakeRegistration(
    [CborIndex(0)] int Tag,
    [CborIndex(1)] Credential StakeCredential
) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record StakeDeregistration(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] Credential StakeCredential
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record StakeDelegation(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] Credential StakeCredential,
        [CborIndex(2)] byte[] PoolKeyHash
    ) : Certificate;


    // [CborSerializable]
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
        [CborIndex(9)][CborNullable] PoolMetadata? PoolMetadata
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record PoolRetirement(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] byte[] PoolKeyHash,
        [CborIndex(2)] ulong EpochNo
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record RegCert(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] Credential StakeCredential,
        [CborIndex(2)] ulong Coin
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record UnRegCert(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] Credential StakeCredential,
        [CborIndex(2)] ulong Coin
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record VoteDelegCert(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] Credential StakeCredential,
        [CborIndex(2)] DRep DRep
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record StakeVoteDelegCert(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] Credential StakeCredential,
        [CborIndex(2)] byte[] PoolKeyHash,
        [CborIndex(3)] DRep DRep
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record StakeRegDelegCert(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] Credential StakeCredential,
        [CborIndex(2)] byte[] PoolKeyHash,
        [CborIndex(3)] ulong Coin
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record VoteRegDelegCert(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] Credential StakeCredential,
        [CborIndex(2)] DRep DRep,
        [CborIndex(3)] ulong Coin
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record StakeVoteRegDelegCert(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] Credential StakeCredential,
        [CborIndex(2)] byte[] PoolKeyHash,
        [CborIndex(3)] DRep Drep,
        [CborIndex(4)] ulong Coin
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record AuthCommitteeHotCert(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] Credential CommitteeColdCredential,
        [CborIndex(2)] Credential CommitteeHotCredential
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record ResignCommitteeColdCert(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] byte[] CommitteeColdCredential,
        [CborIndex(2)] Anchor? Anchor
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record RegDrepCert(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] Credential DRepCredential,
        [CborIndex(2)] ulong Coin,
        [CborIndex(3)][CborNullable] Anchor? Anchor
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record UnRegDrepCert(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] Credential DrepCredential,
        [CborIndex(2)] ulong Coin
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record UpdateDrepCert(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] Credential DrepCredential,
        [CborIndex(2)][CborNullable] Anchor? Anchor
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record GenesisKeyDelegation(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] byte[] GenesisHash,
        [CborIndex(2)] byte[] GenesisDelegateHash,
        [CborIndex(3)] byte[] VrfKeyHash
    ) : Certificate;


    // [CborSerializable]
    [CborList]
    public partial record MoveInstantaneousRewardsCert(
        [CborIndex(0)] int Tag,
        [CborIndex(1)] MoveInstantaneousReward MoveInstantaneousReward
    ) : Certificate;
}

