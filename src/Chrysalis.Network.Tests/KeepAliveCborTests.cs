using Chrysalis.Cbor.Serialization;
using Chrysalis.Network.Cbor.KeepAlive;
using Xunit;

namespace Chrysalis.Network.Tests;

public class KeepAliveCborTests
{
    [Fact]
    public void KeepAlive_Message_Matches_Pallas_Encoding()
    {
        var message = KeepAliveMessages.KeepAlive(0x1234);
        byte[] actual = CborSerializer.Serialize(message);
        byte[] expected = Convert.FromHexString("8200191234");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void KeepAlive_Response_Matches_Pallas_Encoding()
    {
        var message = KeepAliveMessages.KeepAliveResponse(0x1234);
        byte[] actual = CborSerializer.Serialize(message);
        byte[] expected = Convert.FromHexString("8201191234");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void KeepAlive_Done_Matches_Pallas_Encoding()
    {
        var message = KeepAliveMessages.Done();
        byte[] actual = CborSerializer.Serialize(message);
        byte[] expected = Convert.FromHexString("8102");
        Assert.Equal(expected, actual);
    }
}
