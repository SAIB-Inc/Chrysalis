using System.Text.Json;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Tx.Providers;
using Chrysalis.Wallet.Models.Enums;
using Xunit;
using Xunit.Sdk;
using Env = Chrysalis.IntegrationTests.IntegrationEnvironment;

namespace Chrysalis.IntegrationTests;

/// <summary>Provider parameter contracts compared with the configured Preprod node, not baked defaults.</summary>
public sealed class ProviderIntegrationTests
{
    /// <summary>Checks the real Ouroboros provider's parameters against the same node's CLI.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output])]
    [Trait("Category", "Integration")]
    public async Task OuroborosParametersMatchCli()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(40));
        await OracleCli.RecordVersionAsync(deadline.Token);
        Ouroboros provider = new(Env.Required(Env.Socket), Env.NetworkMagic);
        (ProtocolParams expected, ProtocolParams actual) = await OracleCli.AtStableTipAsync("ouroboros-parameters", async (_, ct) =>
            (ProtocolOracles.Parameters(await OracleCli.QueryJsonAsync("ouroboros-parameter-oracle", ct, "protocol-parameters")), await provider.GetParametersAsync(ct).WaitAsync(ct)), deadline.Token);
        ProtocolOracles.AssertEquivalent(expected, actual);
    }

    /// <summary>Reads a RAM-file key in this test process only and makes one modest parameter request.</summary>
    [IntegrationFact([Env.Socket, Env.Magic, Env.Cli, Env.NodeVersion, Env.Output, Env.BlockfrostFile])]
    [Trait("Category", "Integration")]
    [Trait("Known", "F2")]
    public async Task BlockfrostPreprodParametersMatchCli()
    {
        Env.RequirePreprod();
        string keyFile = Env.ValidateCredentialPath(Env.Required(Env.BlockfrostFile), Env.BlockfrostFile);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(60));
        await OracleCli.RecordVersionAsync(deadline.Token);
        JsonElement before = await OracleCli.QueryJsonAsync("blockfrost-epoch-before", deadline.Token, "tip");
        ProtocolParams expected = ProtocolOracles.Parameters(await OracleCli.QueryJsonAsync("blockfrost-parameter-oracle", deadline.Token, "protocol-parameters"));
        string key = (await File.ReadAllTextAsync(keyFile, deadline.Token)).Trim();
        try
        {
            Assert.False(string.IsNullOrWhiteSpace(key));
            using Blockfrost provider = new(key, NetworkType.Preprod);
            ProtocolParams actual = await provider.GetParametersAsync().WaitAsync(deadline.Token);
            JsonElement after = await OracleCli.QueryJsonAsync("blockfrost-epoch-after", deadline.Token, "tip");
            Assert.Equal(before.GetProperty("epoch").GetUInt64(), after.GetProperty("epoch").GetUInt64());
            ProtocolOracles.AssertEquivalent(expected, actual);
        }
        catch (Exception exception)
        {
            // No raw HTTP output is recorded. Never attach the original exception or headers.
            throw new XunitException(SafeDiagnostics.Redact(exception.Message, key));
        }
    }
}
