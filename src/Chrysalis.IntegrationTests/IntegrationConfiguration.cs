using System.Globalization;
using System.Net;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Chrysalis.IntegrationTests;

internal static class IntegrationEnvironment
{
    internal const string Socket = "CHRYSALIS_NODE_SOCKET";
    internal const string Magic = "CHRYSALIS_NETWORK_MAGIC";
    internal const string NodeVersion = "CHRYSALIS_NODE_VERSION";
    internal const string Cli = "CHRYSALIS_CARDANO_CLI";
    internal const string Output = "CHRYSALIS_INTEGRATION_OUTPUT";
    internal const string Host = "CHRYSALIS_N2N_HOST";
    internal const string Port = "CHRYSALIS_N2N_PORT";
    internal const string Address = "CHRYSALIS_QUERY_ADDRESS";
    internal const string FundedOptIn = "CHRYSALIS_IT_FUNDED";
    internal const string CollateralTxIn = "CHRYSALIS_IT_COLLATERAL_TXIN";
    internal const string WalletFile = "CHRYSALIS_WALLET_KEY_FILE";
    internal const string WalletAddress = "CHRYSALIS_WALLET_ADDRESS";
    internal const string BlockfrostFile = "CHRYSALIS_BLOCKFROST_KEY_FILE";

    internal static string Required(string variable) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))
            ? throw new InvalidOperationException($"Required environment variable is missing: {variable}")
            : Environment.GetEnvironmentVariable(variable)!;

    internal static ulong NetworkMagic => ulong.TryParse(Required(Magic), CultureInfo.InvariantCulture, out ulong magic)
        ? magic : throw new InvalidOperationException($"{Magic} must be an unsigned integer.");

    internal static int NodeToNodePort => int.TryParse(Required(Port), CultureInfo.InvariantCulture, out int port) && port is > 0 and <= 65535
        ? port : throw new InvalidOperationException($"{Port} must be a valid TCP port.");

    internal static string OutputDirectory
    {
        get
        {
            string directory = Path.GetFullPath(Required(Output));
            _ = Directory.CreateDirectory(directory);
            return directory;
        }
    }

    internal static string ValidateCredentialPath(string path, string variable)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(variable);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException($"{variable} must be an absolute path supplied by the operator.");
        }
        return path;
    }

    internal static bool IsLoopbackHost(string host) => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address));

    internal static string RequireLoopbackHost()
    {
        string host = Required(Host);
        if (!IsLoopbackHost(host))
        {
            throw new InvalidOperationException($"{Host} must be loopback for the same-node N2N oracle.");
        }
        // Never let DNS redirect a configured localhost to an external peer.
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ? IPAddress.Loopback.ToString() : host;
    }

    internal static bool IsFundedOptIn(string? value) => string.Equals(value, "1", StringComparison.Ordinal);

    internal static void RequireFundedOptIn()
    {
        if (!IsFundedOptIn(Environment.GetEnvironmentVariable(FundedOptIn)))
        {
            throw new InvalidOperationException($"Funded execution requires the explicit per-run opt-in {FundedOptIn}=1.");
        }
    }

    internal static string MissingDescription(string name) => name == Address
        ? name + " (public Preprod reference address known to hold UTxOs; no wallet key is needed)"
        : name == CollateralTxIn ? name + " (public collateral txid#index to exclude from spending regardless of its value)" : name;

    internal static void RequirePreprod()
    {
        if (NetworkMagic != 1)
        {
            throw new InvalidOperationException("This integration route is authorized for Preprod (magic 1) only.");
        }
    }
}

/// <summary>Discovers a real skipped fact, with a reason, when required configuration is absent.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IntegrationFactAttribute : FactAttribute
{
    /// <summary>Initializes an opt-in fact without reading any credential file.</summary>
    /// <param name="requiredEnvironment">Names of the required environment variables.</param>
    public IntegrationFactAttribute(params string[] requiredEnvironment)
    {
        ArgumentNullException.ThrowIfNull(requiredEnvironment);
        RequiredEnvironment = Array.AsReadOnly(requiredEnvironment);
        string[] missing = [.. requiredEnvironment.Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))];
        if (missing.Length != 0)
        {
            Skip = "Missing integration configuration: " + string.Join(", ", missing.Select(IntegrationEnvironment.MissingDescription));
        }
        if (requiredEnvironment.Contains(IntegrationEnvironment.Host, StringComparer.Ordinal)
            && Environment.GetEnvironmentVariable(IntegrationEnvironment.Host) is string host
            && !string.IsNullOrWhiteSpace(host) && !IntegrationEnvironment.IsLoopbackHost(host))
        {
            Skip = (Skip is null ? string.Empty : Skip + "; ")
                + $"{IntegrationEnvironment.Host} must be loopback for a same-node N2N comparison; remote peers are not supported.";
        }
        if (requiredEnvironment.Contains(IntegrationEnvironment.FundedOptIn, StringComparer.Ordinal)
            && !IntegrationEnvironment.IsFundedOptIn(Environment.GetEnvironmentVariable(IntegrationEnvironment.FundedOptIn)))
        {
            Skip = (Skip is null ? string.Empty : Skip + "; ")
                + $"Funded execution disabled: explicitly opt in for this run with {IntegrationEnvironment.FundedOptIn}=1 (run.sh --funded).";
        }
    }

    /// <summary>Gets configuration names only; credential values are never stored here.</summary>
    public IReadOnlyList<string> RequiredEnvironment { get; }
}
