using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Guards the generated-message boundary of the shipped package.</summary>
public sealed class PublicSurfaceTests
{
    /// <summary>Only the provider API is exported; exactly one friend, UtxoRpcBench, is allowed, and no test-side lean type is allowed.</summary>
    [Fact]
    public void ExportsOnlyTheProviderApiAndTheBenchmarkFriend()
    {
        Assembly assembly = typeof(UtxoRpc).Assembly;
        Assert.Equal(
            new[] { typeof(UtxoRpc), typeof(UtxoRpcException), typeof(UtxoRpcServiceCollectionExtensions) }.OrderBy(t => t.FullName, StringComparer.Ordinal),
            assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal));
        Assert.DoesNotContain(typeof(PublicSurfaceTests).Assembly.GetTypes(), type => type.Namespace == "Chrysalis.Tx.Providers.Lean");
        string[] expectedFriends = ["UtxoRpcBench"];
        Assert.Equal(expectedFriends, assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName).Order(StringComparer.Ordinal));
    }
}
