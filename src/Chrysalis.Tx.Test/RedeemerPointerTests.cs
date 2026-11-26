using Chrysalis.Codec.Types;
using Chrysalis.Codec.Extensions.Cardano.Core.TransactionWitness;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Plutus.VM.Models;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Test.Cli;
using Chrysalis.Tx.Test.Providers;
using Xunit;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;

namespace Chrysalis.Tx.Test;

/// <summary>Checks ledger redeemer pointers on transactions built from templates.</summary>
public sealed class RedeemerPointerTests
{
    private static readonly string N = Convert.ToHexString([.. Enumerable.Repeat((byte)0x01, 28)]);
    private static readonly string A = new('A', 56);
    private static readonly string B = new('B', 56);
    private static readonly byte[] ScriptHash = [.. Enumerable.Repeat((byte)0x0C, 28)];
    private static readonly byte[] OtherScriptHash = [.. Enumerable.Repeat((byte)0x0D, 28)];
    private static readonly byte[] PoolHash = [.. Enumerable.Repeat((byte)0x0E, 28)];
    private static readonly string ScriptAccount = "F0" + Convert.ToHexString(ScriptHash);
    private static readonly string KeyAccount = "E0" + Convert.ToHexString([.. Enumerable.Repeat((byte)0x0F, 28)]);

    private static readonly ResolvedInput Funds = new(
        TransactionInput.Create(Convert.FromHexString(BlockfrostTests.Hash), 0),
        PostAlonzoTransactionOutput.Create(new Address(WalletAddress.FromBech32(BlockfrostTests.Address).ToBytes()), Lovelace.Create(1_000_000_000)));

    /// <summary>A certificate is kept alongside a mint redeemer, at its body index.</summary>
    [Fact]
    public async Task CertificateRedeemerIsKeptBesideAMintRedeemer()
    {
        TransactionTemplateBuilder<int> template = Template()
            .AddMint(Mint(A, 1))
            .AddCertificate((_, _) => { })
            .AddCertificate((options, _) =>
            {
                options.Certificate = Delegation(ScriptHash);
                options.RedeemerData = PlutusInt.Create(3);
            });

        Dictionary<(int Tag, ulong Index), string> expected = new() { [(1, 0)] = Data(1), [(2, 0)] = Data(3) };
        Assert.Equal(expected, Pointers(await Build(template)));
    }

    /// <summary>Mint pointers count all policies in byte order and keep the last redeemer per policy.</summary>
    [Fact]
    public async Task MintRedeemersFollowTheSortedPoliciesOnePerPolicy()
    {
        TransactionTemplateBuilder<int> template = Template()
            .AddMint(Mint(B, 2)).AddMint(Mint(N, null)).AddMint(Mint(A, 1)).AddMint(Mint(A, 3));

        Dictionary<(int Tag, ulong Index), string> expected = new() { [(1, 1)] = Data(3), [(1, 2)] = Data(2) };
        Assert.Equal(expected, Pointers(await Build(template)));
    }

    /// <summary>A pre-build hook can still add a redeemer to a certificate-only template.</summary>
    [Fact]
    public async Task HookCertificateRedeemerIsKeptInACertificateOnlyTemplate()
    {
        TransactionTemplateBuilder<int> template = Template()
            .AddCertificate((options, _) =>
            {
                options.Certificate = Delegation(ScriptHash);
                options.RedeemerData = PlutusInt.Create(3);
            })
            .SetPreBuildHook((builder, _, _) =>
            {
                _ = builder.AddCertificate(Delegation(OtherScriptHash));
                _ = builder.RedeemerSet.AddCert(1, PlutusInt.Create(4));
            });

        Dictionary<(int Tag, ulong Index), string> expected = new() { [(2, 0)] = Data(3), [(2, 1)] = Data(4) };
        Assert.Equal(expected, Pointers(await Build(template)));
    }

    /// <summary>TxBuilder indexes withdrawal redeemers in ledger order (script accounts first) on every draft and final tx.</summary>
    [Fact]
    public async Task TxBuilderWithdrawalRedeemerPointsAtTheScriptAccountFirst()
    {
        (PostMaryTransaction tx, List<Dictionary<(int Tag, ulong Index), string>> drafts) = await Complete(builder => builder
            .AddWithdrawal(KeyAccount, 0)
            .AddWithdrawal(ScriptAccount, 0, Convert.ToHexString(ScriptHash), PlutusInt.Create(5)));

        Dictionary<(int Tag, ulong Index), string> expected = new() { [(3, 0)] = Data(5) };
        Assert.Equal(expected, Pointers(tx));
        Assert.NotEmpty(drafts);
        Assert.All(drafts, draft => Assert.Equal(expected, draft));
    }

    /// <summary>Template withdrawal pointers follow the body's ledger order and skip empty-From configs even when an empty alias exists.</summary>
    [Fact]
    public async Task TemplateWithdrawalRedeemerPointsAtTheScriptAccountFirst()
    {
        TransactionTemplateBuilder<int> template = Template()
            .AddStaticParty("", Bech32(ScriptAccount))
            .AddStaticParty("key", Bech32(KeyAccount))
            .AddStaticParty("script", Bech32(ScriptAccount))
            .AddWithdrawal((options, _) => options.From = "key")
            .AddWithdrawal((options, _) =>
            {
                options.From = "script";
                options.SetRedeemerBuilder(_ => PlutusInt.Create(5));
            })
            .AddWithdrawal((options, _) => options.SetRedeemerBuilder(_ => PlutusInt.Create(99)));

        Dictionary<(int Tag, ulong Index), string> expected = new() { [(3, 0)] = Data(5) };
        Assert.Equal(expected, Pointers(await Build(template)));
    }

    private static CliSnapshotProvider Provider() => new(CliVectors.Load(CliVectors.Root).Parameters(CliVectors.G1), [Funds]);

    private static TransactionTemplateBuilder<int> Template() => TransactionTemplateBuilder.Create<int>(Provider())
        .SetChangeAddress(BlockfrostTests.Address)
        .AddOutput((options, _, _) =>
        {
            options.To = "change";
            options.Amount = Lovelace.Create(2_000_000);
        });

    private static MintConfig<int> Mint(string policy, int? redeemer) => (options, _) =>
    {
        options.Policy = policy;
        options.Assets["01"] = 1;
        if (redeemer.HasValue)
        {
            options.SetRedeemerBuilder(_ => PlutusInt.Create(redeemer.Value));
        }
    };

    private static StakeDelegation Delegation(byte[] script) => StakeDelegation.Create(2, Credential.Create(1, script), PoolHash);

    private static async Task<PostMaryTransaction> Build(TransactionTemplateBuilder<int> template) =>
        (PostMaryTransaction)await template.Build(eval: false)(0);

    private static string Bech32(string account) => WalletAddress.FromBytes(Convert.FromHexString(account)).ToBech32();

    private static async Task<(PostMaryTransaction Tx, List<Dictionary<(int Tag, ulong Index), string>> Drafts)> Complete(Func<TxBuilder, TxBuilder> setup)
    {
        List<Dictionary<(int Tag, ulong Index), string>> drafts = [];
        TxBuilder builder = new TxBuilder(Provider())
            .AddUnspentOutputs([Funds])
            .SetChangeAddress(BlockfrostTests.Address)
            .PayLovelace(BlockfrostTests.Address, 2_000_000)
            .UseEvaluator((draft, _) =>
            {
                Dictionary<(int Tag, ulong Index), string> pointers = Pointers(draft);
                drafts.Add(pointers);
                return Task.FromResult<IReadOnlyList<EvaluationResult>>([.. pointers.Keys.Select(k => new EvaluationResult(k.Tag, k.Index, new(0, 0)))]);
            });

        return (await setup(builder).Complete(), drafts);
    }

    private static Dictionary<(int Tag, ulong Index), string> Pointers(PostMaryTransaction tx)
    {
        if (tx.Witnesses.Redeemers() is not { } redeemers)
        {
            return [];
        }

        RedeemerMap map = Assert.IsType<RedeemerMap>(redeemers);
        CborMap<RedeemerKey, RedeemerValue> entries = map.Value;
        // Decoding collapses duplicate keys, so check the original wire count before comparing pointers.
        Assert.Equal(0xA0 + entries.Count, CborSerializer.Serialize(map)[0]);
        return entries.ToDictionary(entry => (entry.Key.Tag, entry.Key.Index), entry => Convert.ToHexString(CborSerializer.Serialize(entry.Value.Data)));
    }

    private static string Data(int number) => Convert.ToHexString(CborSerializer.Serialize(PlutusInt.Create(number)));
}
