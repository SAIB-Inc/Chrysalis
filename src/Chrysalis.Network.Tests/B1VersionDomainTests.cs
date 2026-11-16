using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.Handshake;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>Wire-family discrimination and independent Haskell proposal compatibility for B1.</summary>
public sealed class B1VersionDomainTests
{
    /// <summary>N2C scalar decoding must not accept a value from the node-to-node family.</summary>
    [Theory]
    [InlineData("0e")]
    [InlineData("1a00010000")]
    public void NodeToClientRejectsWrongWireFamily(string hex) =>
        _ = Assert.ThrowsAny<Exception>(() => CborSerializer.Deserialize<N2CVersion>(Convert.FromHexString(hex)));

    /// <summary>Unknown future numbers inside the correct family remain representable explicitly.</summary>
    [Fact]
    public void FutureNumbersInsideTheWireFamilyRemainRepresentable()
    {
        Assert.Equal(32792, CborSerializer.Deserialize<N2CVersion>(Convert.FromHexString("198018")).Value);
        Assert.Equal(15, CborSerializer.Deserialize<N2NVersion>(Convert.FromHexString("0f")).Value);
    }

}
