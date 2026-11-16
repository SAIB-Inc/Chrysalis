using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Multiplexer;
using Xunit;

namespace Chrysalis.Network.Tests;

/// <summary>Startup binding and explicit-offer contracts, exercised against an offline local peer.</summary>
public sealed class B1NodeClientApiTests
{
    /// <summary>Adding the explicit-table overload must not change the binding/default magic of StartAsync().</summary>
    [Fact]
    public async Task NoArgumentStartStillBindsTheMagicOnlyOverload()
    {
        TaskCompletionSource<int> port = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));
        TaskCompletionSource<byte[]> proposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // SYNTHETIC from pinned OND CDDL: accept V20/magic 2; the peer offers only V20.
        Task peer = ReplyAsync(port, Convert.FromHexString("83011980148202f4"), proposed, release.Task, deadline.Token);
        try
        {
            using NodeClient client = await NodeClient.ConnectAsync("127.0.0.1", await port.Task.WaitAsync(deadline.Token), deadline.Token);
            await client.StartAsync().WaitAsync(deadline.Token); // Deliberately no arguments: compiler binding is part of the test.
            Assert.Equal(2UL, client.NetworkMagic);
            ProposeVersions proposal = CborSerializer.Deserialize<ProposeVersions>(await proposed.Task.WaitAsync(deadline.Token));
            N2CVersionTable table = Assert.IsType<N2CVersionTable>(proposal.VersionTable);
            Assert.All(table.Value.Values, data => Assert.Equal(2UL, data.NetworkMagic));
        }
        finally
        {
            _ = release.TrySetResult();
            await peer;
        }
    }

    /// <summary>Negotiated state is exposed without a public setter or an assumed pre-handshake value.</summary>
    [Fact]
    public void NegotiatedVersionHasNoPublicSetter()
    {
        PropertyInfo? property = typeof(NodeClient).GetProperty("NegotiatedVersion");
        Assert.NotNull(property);
        Assert.Equal(typeof(N2CVersion), property.PropertyType);
        Assert.Null(property.GetSetMethod());
    }

    private static async Task ReplyAsync(TaskCompletionSource<int> port, byte[] reply, TaskCompletionSource<byte[]> proposed, Task release, CancellationToken cancellationToken)
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        _ = port.TrySetResult(((IPEndPoint)listener.LocalEndpoint).Port);
        using TcpClient socket = await listener.AcceptTcpClientAsync(cancellationToken);
        using NetworkStream stream = socket.GetStream();
        byte[] header = new byte[8];
        await stream.ReadExactlyAsync(header, cancellationToken);
        byte[] request = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6, 2))];
        await stream.ReadExactlyAsync(request, cancellationToken);
        _ = proposed.TrySetResult(request);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4, 2), 0x8000);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6, 2), checked((ushort)reply.Length));
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(reply, cancellationToken);
        await release.WaitAsync(cancellationToken);
    }
}
