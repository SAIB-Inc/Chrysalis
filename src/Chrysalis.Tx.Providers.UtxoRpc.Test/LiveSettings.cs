using CardanoNetwork = Chrysalis.Wallet.Models.Enums.NetworkType;
using Xunit;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Owns the opt-in live configuration and its names.</summary>
internal static class LiveSettings
{
    internal const string Endpoint = "CHRYSALIS_UTXORPC_ENDPOINT";
    internal const string Network = "CHRYSALIS_UTXORPC_NETWORK";
    internal const string Headers = "CHRYSALIS_UTXORPC_HEADERS";
    internal const string Addresses = "CHRYSALIS_TEST_WALLET_ADDRESS";
    internal const string BlockfrostKeyFile = "CHRYSALIS_TEST_BLOCKFROST_KEY_FILE";
    internal const string WalletKeyFile = "CHRYSALIS_TEST_WALLET_KEY_FILE";
    internal const string FeeLedgerFile = "CHRYSALIS_TEST_FEE_LEDGER_FILE";
    internal const string FeeBudget = "CHRYSALIS_TEST_FEE_BUDGET_LOVELACE";
    internal const string RecordDirectory = "CHRYSALIS_UTXORPC_RECORD_DIR";
    internal const string RecordAddress = "CHRYSALIS_UTXORPC_RECORD_ADDRESS";
    internal const string RecordTransaction = "CHRYSALIS_UTXORPC_RECORD_TX";
    internal const string RecordHashReferences = "CHRYSALIS_UTXORPC_RECORD_HASH_REFS";

    internal static string? Read(string name) => Environment.GetEnvironmentVariable(name);
    internal static string Require(string name) => !string.IsNullOrWhiteSpace(Read(name)) ? Read(name)! : throw new InvalidOperationException($"Missing {name}.");
    internal static List<string> WalletAddresses() => [.. Require(Addresses).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    internal static UtxoRpc CreateProvider() => new(new Uri(Require(Endpoint)), ReadNetwork(), HeaderValues());

    internal static CardanoNetwork ReadNetwork()
    {
        if (!string.Equals(Require(Network), nameof(CardanoNetwork.Preprod), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Live tests require Preprod.");
        }
        return CardanoNetwork.Preprod;
    }

    internal static Dictionary<string, string> HeaderValues()
    {
        Dictionary<string, string> headers = [with(StringComparer.Ordinal)];
        foreach (string pair in (Read(Headers) ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int split = pair.IndexOf('=', StringComparison.Ordinal);
            if (split <= 0) { throw new InvalidOperationException($"Invalid {Headers}; expected name=value pairs."); }
            headers.Add(pair[..split], pair[(split + 1)..]);
        }
        return headers;
    }
}

/// <summary>Skips an opt-in test at discovery when required configuration is absent.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class LiveFactAttribute : FactAttribute
{
    /// <summary>Names each setting needed to run this test.</summary>
    /// <param name="required">Required configuration names.</param>
    public LiveFactAttribute(params string[] required)
    {
        Required = required;
        foreach (string name in required)
        {
            if (string.IsNullOrWhiteSpace(LiveSettings.Read(name))) { Skip = $"Missing {name}."; break; }
        }
    }

    /// <summary>Gets the required configuration names.</summary>
    public string[] Required { get; }
}
