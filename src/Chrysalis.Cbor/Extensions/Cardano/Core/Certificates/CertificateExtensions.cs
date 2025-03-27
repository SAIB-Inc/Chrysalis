using Chrysalis.Cbor.Types.Cardano.Core.Certificates;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using CCertificate = Chrysalis.Cbor.Types.Cardano.Core.Certificates.Certificate;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Certificates;

public static class CertificateExtensions
{
    public static int Tag(this CCertificate self) =>
        self switch
        {
            StakeRegistration stakeRegistration => stakeRegistration.Tag,
            StakeDeregistration stakeDeregistration => stakeDeregistration.Tag,
            StakeDelegation stakeDelegation => stakeDelegation.Tag,
            PoolRegistration poolRegistration => poolRegistration.Tag,
            PoolRetirement poolRetirement => poolRetirement.Tag,
            RegCert regCert => regCert.Tag,
            UnRegCert unRegCert => unRegCert.Tag,
            VoteDelegCert voteDelegCert => voteDelegCert.Tag,
            StakeRegDelegCert stakeRegDelegCert => stakeRegDelegCert.Tag,
            VoteRegDelegCert voteRegDelegCert => voteRegDelegCert.Tag,
            StakeVoteRegDelegCert stakeVoteRegDelegCert => stakeVoteRegDelegCert.Tag,
            AuthCommitteeHotCert authCommitteeHotCert => authCommitteeHotCert.Tag,
            ResignCommitteeColdCert resignCommitteeColdCert => resignCommitteeColdCert.Tag,
            RegDrepCert regDrepCert => regDrepCert.Tag,
            UnRegDrepCert unRegDrepCert => unRegDrepCert.Tag,
            UpdateDrepCert updateDrepCert => updateDrepCert.Tag,
            _ => throw new NotImplementedException()
        };

    public static Credential? StakeCredential(this CCertificate self) =>
        self switch
        {
            StakeRegistration stakeRegistration => stakeRegistration.StakeCredential,
            StakeDeregistration stakeDeregistration => stakeDeregistration.StakeCredential,
            StakeDelegation stakeDelegation => stakeDelegation.StakeCredential,
            RegCert regCert => regCert.StakeCredential,
            UnRegCert unRegCert => unRegCert.StakeCredential,
            VoteDelegCert voteDelegCert => voteDelegCert.StakeCredential,
            StakeRegDelegCert stakeRegDelegCert => stakeRegDelegCert.StakeCredential,
            VoteRegDelegCert voteRegDelegCert => voteRegDelegCert.StakeCredential,
            StakeVoteRegDelegCert stakeVoteRegDelegCert => stakeVoteRegDelegCert.StakeCredential,
            _ => null
        };

    public static Credential? DRepCredential(this CCertificate self) =>
        self switch
        {
            RegDrepCert regDrepCert => regDrepCert.DRepCredential,
            UnRegDrepCert unRegDrepCert => unRegDrepCert.DrepCredential,
            UpdateDrepCert updateDrepCert => updateDrepCert.DrepCredential,
            _ => null
        };

    public static byte[]? PoolKeyHash(this CCertificate self) =>
        self switch
        {
            StakeDelegation stakeDelegation => stakeDelegation.PoolKeyHash,
            PoolRetirement poolRetirement => poolRetirement.PoolKeyHash,
            StakeVoteDelegCert stakeVoteDelegCert => stakeVoteDelegCert.PoolKeyHash,
            StakeRegDelegCert stakeRegDelegCert => stakeRegDelegCert.PoolKeyHash,
            StakeVoteRegDelegCert stakeVoteRegDelegCert => stakeVoteRegDelegCert.PoolKeyHash,
            _ => null
        };

    public static ulong? Coin(this CCertificate self) =>
        self switch
        {
            RegCert regCert => regCert.Coin,
            UnRegCert unRegCert => unRegCert.Coin,
            StakeRegDelegCert stakeRegDelegCert => stakeRegDelegCert.Coin,
            VoteRegDelegCert voteRegDelegCert => voteRegDelegCert.Coin,
            StakeVoteRegDelegCert stakeVoteRegDelegCert => stakeVoteRegDelegCert.Coin,
            RegDrepCert regDrepCert => regDrepCert.Coin,
            UnRegDrepCert unRegDrepCert => unRegDrepCert.Coin,
            _ => null
        };

    public static DRep? DRep(this CCertificate self) =>
        self switch
        {
            VoteDelegCert voteDelegCert => voteDelegCert.DRep,
            StakeVoteDelegCert stakeVoteDelegCert => stakeVoteDelegCert.DRep,
            VoteRegDelegCert voteRegDelegCert => voteRegDelegCert.DRep,
            StakeVoteRegDelegCert stakeVoteRegDelegCert => stakeVoteRegDelegCert.Drep,
            _ => null
        };

    public static Anchor? Anchor(this CCertificate self) =>
        self switch
        {
            ResignCommitteeColdCert resignCommitteeColdCert => resignCommitteeColdCert.Anchor,
            RegDrepCert regDrepCert => regDrepCert.Anchor,
            UpdateDrepCert updateDrepCert => updateDrepCert.Anchor,
            _ => null
        };
}