using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>Guards the generated-message boundary of the shipped package.</summary>
public sealed class PublicSurfaceTests
{
    /// <summary>Only the provider and its exception are exported; no friend assembly is allowed.</summary>
    [Fact]
    public void ExportsOnlyTheProviderAndExceptionWithoutFriends()
    {
        Assembly assembly = typeof(UtxoRpc).Assembly;
        Assert.Equal(
            new[] { typeof(UtxoRpc), typeof(UtxoRpcException) }.OrderBy(t => t.FullName, StringComparer.Ordinal),
            assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal));
        string[] expectedFriends = [];
        Assert.Equal(expectedFriends, assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName).Order(StringComparer.Ordinal));
    }
}
