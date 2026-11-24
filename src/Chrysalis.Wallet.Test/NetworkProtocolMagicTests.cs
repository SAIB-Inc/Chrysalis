using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Test;

public class NetworkProtocolMagicTests
{
    // cardano-api c727cd6, cardano-rpc/src/Cardano/Rpc/Server/Internal/UtxoRpc/Query.hs:301-306.
    [Theory]
    [InlineData("Mainnet", 764824073)]
    [InlineData("Preprod", 1)]
    [InlineData("Preview", 2)]
    public void Magic_IsTheNetworksOwn(string network, int magic) =>
        Assert.Equal(magic, (int)Enum.Parse<NetworkProtocolMagic>(network));

    [Fact]
    public void Testnet_IsNotAMember() =>
        Assert.DoesNotContain("Testnet", Enum.GetNames<NetworkProtocolMagic>());
}
