using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;
using Xunit;

namespace Chrysalis.Tx.Test.Fees;

public sealed class CertificateCredentialTests
{
    [Fact]
    public void AllCertificateVariantsHaveADeliberateWitnessRule()
    {
        Credential author = Credential.Create(1, new byte[28]);
        Credential hot = Credential.Create(0, Enumerable.Repeat((byte)1, 28).ToArray());
        byte[] pool = new byte[28];
        Abstain drep = Abstain.Create(2);
        (ICertificate Certificate, string Rule)[] cases =
        [
            (StakeRegistration.Create(0, author), "none"),
            (StakeDeregistration.Create(1, author), "author"),
            (StakeDelegation.Create(2, author, pool), "author"),
            (default(PoolRegistration), "none"),
            (PoolRetirement.Create(4, pool, 1), "none"),
            (default(GenesisKeyDelegation), "reject"),
            (default(MoveInstantaneousRewardsCert), "reject"),
            (RegCert.Create(7, author, 1), "author"),
            (UnRegCert.Create(8, author, 1), "author"),
            (VoteDelegCert.Create(9, author, drep), "author"),
            (StakeVoteDelegCert.Create(10, author, pool, drep), "author"),
            (StakeRegDelegCert.Create(11, author, pool, 1), "author"),
            (VoteRegDelegCert.Create(12, author, drep, 1), "author"),
            (StakeVoteRegDelegCert.Create(13, author, pool, drep, 1), "author"),
            (AuthCommitteeHotCert.Create(14, author, hot), "author"),
            (ResignCommitteeColdCert.Create(15, author, null), "author"),
            (RegDrepCert.Create(16, author, 1, null), "author"),
            (UnRegDrepCert.Create(17, author, 1), "author"),
            (UpdateDrepCert.Create(18, author, null), "author")
        ];
        Assert.Equal(typeof(ICertificate).Assembly.GetTypes().Where(t => !t.IsInterface && typeof(ICertificate).IsAssignableFrom(t))
            .Select(t => t.Name).Order(StringComparer.Ordinal), cases.Select(c => c.Certificate.GetType().Name).Order(StringComparer.Ordinal));
        foreach ((ICertificate certificate, string rule) in cases)
        {
            if (rule == "reject")
            {
                Assert.Throws<NotSupportedException>(() => CertificateCredential.WitnessCredential(certificate));
            }
            else if (rule == "author")
            {
                Credential actual = Assert.IsType<Credential>(CertificateCredential.WitnessCredential(certificate));
                Assert.Equal(author.Raw.ToArray(), actual.Raw.ToArray());
            }
            else
            {
                Assert.Null(CertificateCredential.WitnessCredential(certificate));
            }
        }
    }
}
