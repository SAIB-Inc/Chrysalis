using Chrysalis.Codec.Extensions.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Tx.Builders;
using Xunit;

namespace Chrysalis.Tx.Test;

public class TransactionBuilderTests
{
    private static readonly byte[] FakeAddress = new byte[57];

    private static TransactionInput MakeInput(byte hashByte, ulong index)
    {
        byte[] hash = new byte[32];
        hash[0] = hashByte;
        return TransactionInput.Create(hash, index);
    }

    private static AlonzoTransactionOutput MakeOutput(ulong lovelace) =>
        AlonzoTransactionOutput.Create(
            Address.Create(FakeAddress),
            Lovelace.Create(lovelace),
            null);

    private static PlutusV3Script MakeScript()
    {
        byte[] bytes = new byte[32];
        bytes[0] = 0xAA;
        return PlutusV3Script.Create(24, bytes);
    }

    private static StakeRegistration MakeCertificate() =>
        StakeRegistration.Create(0, Credential.Create(0, new byte[28]));

    // ── Auto-integrate RedeemerSet ──

    [Fact]
    public void BuildAutoIntegratesRedeemerSet()
    {
        TransactionBuilder builder = new();

        InputBuilderResult input = new InputBuilder(MakeInput(1, 0), MakeOutput(10_000_000))
            .PlutusScript(MakeScript(), PlutusInt.Create(0), PlutusInt.Create(1));

        builder
            .AddInput(input)
            .AddOutput(MakeOutput(5_000_000))
            .SetFee(200_000);

        // RedeemerSet has redeemers, Redeemers is null
        Assert.True(builder.RedeemerSet.HasRedeemers);
        Assert.Null(builder.Redeemers);

        // Build() should auto-integrate
        _ = builder.Build();
        Assert.NotNull(builder.Redeemers);
    }

    [Fact]
    public void BuildDoesNotOverrideManualRedeemers()
    {
        TransactionBuilder builder = new();
        IRedeemers manualRedeemers = RedeemerList.Create([
            RedeemerEntry.Create(0, 0, PlutusInt.Create(42), ExUnits.Create(100, 200))
        ]);

        builder.SetRedeemers(manualRedeemers);

        // Add an input with redeemer to RedeemerSet
        InputBuilderResult input = new InputBuilder(MakeInput(1, 0), MakeOutput(10_000_000))
            .PlutusScript(MakeScript(), PlutusInt.Create(0), PlutusInt.Create(1));
        builder.AddInput(input);

        builder
            .AddOutput(MakeOutput(5_000_000))
            .SetFee(200_000);

        _ = builder.Build();

        // Manual redeemers should take precedence
        Assert.Same(manualRedeemers, builder.Redeemers);
    }

    // ── AddMint with script ──

    [Fact]
    public void AddMintWithScriptAutoTracksRedeemerAndScript()
    {
        TransactionBuilder builder = new();
        PlutusV3Script script = MakeScript();
        string policyHex = "AABBCCDD";

        builder.AddMint(policyHex, "00", 1, script, PlutusInt.Create(0));

        Assert.NotNull(builder.Mint);
        Assert.True(builder.RedeemerSet.HasRedeemers);
    }

    [Fact]
    public void AddMintMultiAssetWithScriptAutoTracks()
    {
        TransactionBuilder builder = new();
        PlutusV3Script script = MakeScript();
        MultiAssetMint mint = MintBuilder.Create()
            .AddToken("AABBCCDD", "00", 1)
            .AddToken("AABBCCDD", "01", 5)
            .Build();

        builder.AddMint(mint, script, PlutusInt.Create(0));

        Assert.NotNull(builder.Mint);
        Assert.True(builder.RedeemerSet.HasRedeemers);
    }

    // ── AddCertificate with script ──

    [Fact]
    public void AddCertificateWithScriptAutoTracksRedeemer()
    {
        TransactionBuilder builder = new();
        PlutusV3Script script = MakeScript();

        builder.AddCertificate(MakeCertificate(), script, PlutusInt.Create(0));

        Assert.True(builder.RedeemerSet.HasRedeemers);
        Assert.NotNull(builder.Certificates);
    }

    [Fact]
    public void AddCertificateWithScriptAndSignersTracksSigners()
    {
        TransactionBuilder builder = new();
        PlutusV3Script script = MakeScript();
        string signerHex = "AABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDD";

        builder.AddCertificate(MakeCertificate(), script, PlutusInt.Create(0), signerHex);

        Assert.NotNull(builder.RequiredSigners);
        Assert.Single(builder.RequiredSigners);
    }

    // ── AddWithdrawal ──

    [Fact]
    public void AddWithdrawalSimpleBuildsSuccessfully()
    {
        TransactionBuilder builder = new();
        string rewardAddr = "E0AABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDD";

        builder
            .AddWithdrawal(rewardAddr, 5_000_000)
            .AddInput(MakeInput(0, 0))
            .AddOutput(MakeOutput(4_000_000))
            .SetFee(200_000);

        // Should not throw — withdrawal integrated at build time
        _ = builder.Build();
    }

    [Fact]
    public void AddWithdrawalWithScriptAutoTracksRedeemer()
    {
        TransactionBuilder builder = new();
        PlutusV3Script script = MakeScript();
        string rewardAddr = "E0AABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDDAABBCCDD";

        builder.AddWithdrawal(rewardAddr, 5_000_000, script, PlutusInt.Create(0));

        Assert.True(builder.RedeemerSet.HasRedeemers);
    }

    // ── AddMetadata ──

    [Fact]
    public void AddMetadataIntegratesIntoAuxiliaryData()
    {
        TransactionBuilder builder = new();

        builder
            .AddInput(MakeInput(0, 0))
            .AddOutput(MakeOutput(4_000_000))
            .SetFee(200_000)
            .AddMetadata(674, MetadataText.Create("test message"));

        PostMaryTransaction tx = builder.Build();
        Assert.NotNull(tx.AuxiliaryData);
    }

    [Fact]
    public void AddMultipleMetadataLabels()
    {
        TransactionBuilder builder = new();

        builder
            .AddInput(MakeInput(0, 0))
            .AddOutput(MakeOutput(4_000_000))
            .SetFee(200_000)
            .AddMetadata(674, MetadataText.Create("msg"))
            .AddMetadata(721, MetadataText.Create("nft"));

        PostMaryTransaction tx = builder.Build();
        Assert.NotNull(tx.AuxiliaryData);
    }

    // ── Certificate Deposit/Refund ──

    private static readonly byte[] FakeCredentialHash = new byte[28];

    [Fact]
    public void GetDeposit_RegCert_ReturnsCoinValue()
    {
        // Conway RegCert (tag 7) carries deposit in the certificate itself
        RegCert cert = RegCert.Create(7, Credential.Create(0, FakeCredentialHash), 2_000_000);
        Assert.Equal(2_000_000UL, cert.GetDeposit(2_000_000, 500_000_000));
    }

    [Fact]
    public void GetDeposit_StakeRegistration_ReturnsKeyDeposit()
    {
        // Legacy StakeRegistration uses protocol parameter keyDeposit
        StakeRegistration cert = StakeRegistration.Create(0, Credential.Create(0, FakeCredentialHash));
        Assert.Equal(2_000_000UL, cert.GetDeposit(2_000_000, 500_000_000));
    }

    [Fact]
    public void GetDeposit_StakeDelegation_ReturnsZero()
    {
        // Non-registration certs return 0 deposit
        StakeDelegation cert = StakeDelegation.Create(2, Credential.Create(0, FakeCredentialHash), new byte[28]);
        Assert.Equal(0UL, cert.GetDeposit(2_000_000, 500_000_000));
    }

    [Fact]
    public void GetRefund_UnRegCert_ReturnsCoinValue()
    {
        // Conway UnRegCert (tag 8) carries refund in the certificate
        UnRegCert cert = UnRegCert.Create(8, Credential.Create(0, FakeCredentialHash), 2_000_000);
        Assert.Equal(2_000_000UL, cert.GetRefund(2_000_000, 500_000_000));
    }

    [Fact]
    public void GetRefund_StakeDeregistration_ReturnsKeyDeposit()
    {
        // Legacy StakeDeregistration refunds keyDeposit from protocol params
        StakeDeregistration cert = StakeDeregistration.Create(1, Credential.Create(0, FakeCredentialHash));
        Assert.Equal(2_000_000UL, cert.GetRefund(2_000_000, 500_000_000));
    }

    [Fact]
    public void GetRefund_RegCert_ReturnsZero()
    {
        // Registration certs do not produce refunds
        RegCert cert = RegCert.Create(7, Credential.Create(0, FakeCredentialHash), 2_000_000);
        Assert.Equal(0UL, cert.GetRefund(2_000_000, 500_000_000));
    }

    [Fact]
    public void GetDeposit_UnRegCert_ReturnsZero()
    {
        // Deregistration certs do not require deposits
        UnRegCert cert = UnRegCert.Create(8, Credential.Create(0, FakeCredentialHash), 2_000_000);
        Assert.Equal(0UL, cert.GetDeposit(2_000_000, 500_000_000));
    }
}
