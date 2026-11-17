using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Providers;
using Xunit;

namespace Chrysalis.Tx.Test;

public class PackageBoundaryTests
{
    [Fact]
    public void ProtocolParams_BelongsToCodec()
    {
        Assert.Equal(typeof(CborRecord).Assembly, typeof(ProtocolParams).Assembly);
        Assert.Equal("Chrysalis.Codec.Types.Cardano.Core.Protocol", typeof(ProtocolParams).Namespace);
    }

    [Fact]
    public void TransactionBuilder_DoesNotReferenceNetworkOrProviders() =>
        Assert.DoesNotContain(typeof(TransactionBuilder).Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "Chrysalis.Network"
                || reference.Name!.StartsWith("Chrysalis.Tx.Providers.", StringComparison.Ordinal));

    [Theory]
    [InlineData(typeof(Blockfrost), "Chrysalis.Tx.Providers.Blockfrost")]
    [InlineData(typeof(Kupmios), "Chrysalis.Tx.Providers.Kupmios")]
    [InlineData(typeof(Ouroboros), "Chrysalis.Tx.Providers.Ouroboros")]
    public void Provider_BelongsToItsOwnAssembly(Type provider, string assemblyName)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Assert.Equal(assemblyName, provider.Assembly.GetName().Name);
    }

    [Fact]
    public void ProviderDispatcher_BelongsToUmbrella() =>
        Assert.Equal("Chrysalis", typeof(ServiceCollectionExtensions).Assembly.GetName().Name);
}
