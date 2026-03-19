using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using CCertificate = Chrysalis.Codec.Types.Cardano.Core.Certificates.ICertificate;

namespace Chrysalis.Codec.Extensions.Cardano.Core.Certificates;

/// <summary>
/// Extension methods for <see cref="CCertificate"/> to access certificate properties across types.
/// </summary>
public static class CertificateExtensions
{
    /// <summary>
    /// Gets the certificate type tag.
    /// </summary>
    /// <param name="self">The certificate instance.</param>
    /// <returns>The type tag value.</returns>
    public static int Tag(this CCertificate self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
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
    }

    /// <summary>
    /// Gets the stake credential from the certificate, if applicable.
    /// </summary>
    /// <param name="self">The certificate instance.</param>
    /// <returns>The stake credential, or null.</returns>
    public static Credential? StakeCredential(this CCertificate self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
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
    }

    /// <summary>
    /// Gets the DRep credential from the certificate, if applicable.
    /// </summary>
    /// <param name="self">The certificate instance.</param>
    /// <returns>The DRep credential, or null.</returns>
    public static Credential? DRepCredential(this CCertificate self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            RegDrepCert regDrepCert => regDrepCert.DRepCredential,
            UnRegDrepCert unRegDrepCert => unRegDrepCert.DRepCredential,
            UpdateDrepCert updateDrepCert => updateDrepCert.DRepCredential,
            _ => null
        };
    }

    /// <summary>
    /// Gets the pool key hash from the certificate, if applicable.
    /// </summary>
    /// <param name="self">The certificate instance.</param>
    /// <returns>The pool key hash bytes, or null.</returns>
    public static ReadOnlyMemory<byte>? PoolKeyHash(this CCertificate self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            StakeDelegation stakeDelegation => stakeDelegation.PoolKeyHash,
            PoolRetirement poolRetirement => poolRetirement.PoolKeyHash,
            StakeVoteDelegCert stakeVoteDelegCert => stakeVoteDelegCert.PoolKeyHash,
            StakeRegDelegCert stakeRegDelegCert => stakeRegDelegCert.PoolKeyHash,
            StakeVoteRegDelegCert stakeVoteRegDelegCert => stakeVoteRegDelegCert.PoolKeyHash,
            _ => null
        };
    }

    /// <summary>
    /// Gets the coin deposit amount from the certificate, if applicable.
    /// </summary>
    /// <param name="self">The certificate instance.</param>
    /// <returns>The coin amount, or null.</returns>
    public static ulong? Coin(this CCertificate self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
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
    }

    /// <summary>
    /// Returns the deposit amount this certificate requires, or 0.
    /// Conway certs (RegCert, etc.) carry the deposit in the certificate itself.
    /// Legacy certs (StakeRegistration, PoolRegistration) use protocol parameters.
    /// </summary>
    public static ulong GetDeposit(this CCertificate self, ulong keyDeposit, ulong poolDeposit)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            StakeRegistration => keyDeposit,
            PoolRegistration => poolDeposit,
            RegCert r => r.Coin,
            StakeRegDelegCert r => r.Coin,
            VoteRegDelegCert r => r.Coin,
            StakeVoteRegDelegCert r => r.Coin,
            RegDrepCert r => r.Coin,
            _ => 0
        };
    }

    /// <summary>
    /// Returns the refund amount this certificate returns, or 0.
    /// Deregistration certificates refund the original deposit.
    /// </summary>
    public static ulong GetRefund(this CCertificate self, ulong keyDeposit, ulong poolDeposit)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            StakeDeregistration => keyDeposit,
            PoolRetirement => poolDeposit,
            UnRegCert r => r.Coin,
            UnRegDrepCert r => r.Coin,
            _ => 0
        };
    }

    /// <summary>
    /// Gets the DRep from the certificate, if applicable.
    /// </summary>
    /// <param name="self">The certificate instance.</param>
    /// <returns>The DRep, or null.</returns>
    public static IDRep? DRep(this CCertificate self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            VoteDelegCert voteDelegCert => voteDelegCert.DRep,
            StakeVoteDelegCert stakeVoteDelegCert => stakeVoteDelegCert.DRep,
            VoteRegDelegCert voteRegDelegCert => voteRegDelegCert.DRep,
            StakeVoteRegDelegCert stakeVoteRegDelegCert => stakeVoteRegDelegCert.DRep,
            _ => null
        };
    }

    /// <summary>
    /// Gets the anchor from the certificate, if applicable.
    /// </summary>
    /// <param name="self">The certificate instance.</param>
    /// <returns>The anchor, or null.</returns>
    public static Anchor? Anchor(this CCertificate self)
    {
        ArgumentNullException.ThrowIfNull(self);
        return self switch
        {
            ResignCommitteeColdCert resignCommitteeColdCert => resignCommitteeColdCert.Anchor,
            RegDrepCert regDrepCert => regDrepCert.Anchor,
            UpdateDrepCert updateDrepCert => updateDrepCert.Anchor,
            _ => null
        };
    }
}
