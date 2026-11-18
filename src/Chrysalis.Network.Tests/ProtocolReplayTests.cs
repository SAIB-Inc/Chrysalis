using Chrysalis.Network.Core;
using Xunit;
using Xunit.Sdk;

namespace Chrysalis.Network.Tests;

public sealed class ProtocolReplayTests
{
    [Theory]
    [InlineData("00000000000000")] // Short header.
    [InlineData("000000000000000201")] // Short body.
    [InlineData("000000000000000101000000000000000102")] // Two frames instead of one.
    public async Task CompletedSendMustContainExactlyOneFullFrame(string wire)
    {
        using ProtocolReplay replay = new(ProtocolType.Handshake);
        await replay.Channel.WriteRawSegmentAsync(Convert.FromHexString(wire));
        _ = Assert.ThrowsAny<XunitException>(replay.ReadSentPayload);
    }
}
