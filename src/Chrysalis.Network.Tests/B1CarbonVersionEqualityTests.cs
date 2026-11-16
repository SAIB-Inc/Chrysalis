using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.Handshake;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>Wire version identity is numeric, not the decoder's preserved Raw memory slice.</summary>
public sealed class B1CarbonVersionEqualityTests
{
    /// <summary>Decoded N2C accepts and query tables work with the public well-known constants.</summary>
    [Fact]
    public void DecodedN2CVersionEqualsConstantAndFindsDictionaryKey()
    {
        N2CAcceptVersion accept = CborSerializer.Deserialize<N2CAcceptVersion>(Convert.FromHexString("830119801782182af4"));
        Assert.True(accept.Version == N2CVersions.V23);
        Assert.True(N2CVersions.V23 == accept.Version);
        Assert.Equal(N2CVersions.V23.GetHashCode(), accept.Version.GetHashCode());
        N2CQueryReply reply = CborSerializer.Deserialize<N2CQueryReply>(Convert.FromHexString("8203a119801782182af4"));
        Assert.True(reply.VersionTable.Value.ContainsKey(N2CVersions.V23));
        Assert.True(reply.VersionTable.Value.TryGetValue(N2CVersions.V23, out N2CVersionData? data));
        Assert.Equal(42UL, Assert.IsType<N2CVersionData>(data).NetworkMagic);
        Assert.True(accept.Version == Assert.Single(reply.VersionTable.Value).Key);
        Assert.NotEqual(N2CVersions.V22, accept.Version);
    }

    /// <summary>The corresponding N2N accept/table contracts have the same numeric identity.</summary>
    [Fact]
    public void DecodedN2NVersionEqualsConstantAndFindsDictionaryKey()
    {
        N2NAcceptVersion accept = CborSerializer.Deserialize<N2NAcceptVersion>(Convert.FromHexString("83010e84182af500f4"));
        Assert.True(accept.Version == N2NVersions.V14);
        Assert.True(N2NVersions.V14 == accept.Version);
        Assert.Equal(N2NVersions.V14.GetHashCode(), accept.Version.GetHashCode());
        N2NQueryReply reply = CborSerializer.Deserialize<N2NQueryReply>(Convert.FromHexString("8203a10e84182af500f4"));
        Assert.True(reply.VersionTable.Value.ContainsKey(N2NVersions.V14));
        Assert.True(reply.VersionTable.Value.TryGetValue(N2NVersions.V14, out N2NVersionData? data));
        Assert.Equal(42UL, Assert.IsType<N2NVersionData>(data).NetworkMagic);
        Assert.True(accept.Version == Assert.Single(reply.VersionTable.Value).Key);
        Assert.NotEqual(N2NVersions.V13, accept.Version);
    }
}
