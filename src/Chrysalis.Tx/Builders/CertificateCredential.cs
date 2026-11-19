using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;

namespace Chrysalis.Tx.Builders;

internal static class CertificateCredential
{
    // Conway/TxCert.hs: witness author, not necessarily the credential receiving a delegation.
    internal static Credential? WitnessCredential(ICertificate certificate) => certificate switch
    {
        StakeRegistration or PoolRegistration or PoolRetirement => null,
        StakeDeregistration c => c.StakeCredential,
        StakeDelegation c => c.StakeCredential,
        RegCert c => c.StakeCredential,
        UnRegCert c => c.StakeCredential,
        VoteDelegCert c => c.StakeCredential,
        StakeVoteDelegCert c => c.StakeCredential,
        StakeRegDelegCert c => c.StakeCredential,
        VoteRegDelegCert c => c.StakeCredential,
        StakeVoteRegDelegCert c => c.StakeCredential,
        AuthCommitteeHotCert c => c.ColdCredential,
        ResignCommitteeColdCert c => c.ColdCredential,
        RegDrepCert c => c.DRepCredential,
        UnRegDrepCert c => c.DRepCredential,
        UpdateDrepCert c => c.DRepCredential,
        GenesisKeyDelegation or MoveInstantaneousRewardsCert => throw new NotSupportedException("Pre-Conway certificate is not supported."),
        _ => throw new NotSupportedException($"Unsupported Conway certificate {certificate.GetType().Name}.")
    };
}
