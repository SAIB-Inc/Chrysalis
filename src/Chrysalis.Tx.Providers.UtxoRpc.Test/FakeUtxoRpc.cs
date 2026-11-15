using System.Net;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Utxorpc.V1Beta.Cardano;
using Utxorpc.V1Beta.Query;

namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>A real HTTP/2 loopback server with a per-test parameter handler.</summary>
internal sealed class FakeUtxoRpc : QueryService.QueryServiceBase, IAsyncDisposable
{
    private WebApplication? _app;
    private int _requestCount;

    internal Uri Endpoint { get; private set; } = null!;

    internal int RequestCount => Volatile.Read(ref _requestCount);

    internal Func<ReadParamsRequest, ServerCallContext, Task<ReadParamsResponse>> Handler { get; set; } =
        static (_, _) => Task.FromResult(Answer(new PParams()));

    internal static ReadParamsResponse Answer(PParams parameters) => new()
    {
        Values = new AnyChainParams { Cardano = parameters }
    };

    internal static async Task<FakeUtxoRpc> StartAsync()
    {
        FakeUtxoRpc fake = new();
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        _ = builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, l => l.Protocols = HttpProtocols.Http2));
        _ = builder.Logging.ClearProviders();
        _ = builder.Services.AddGrpc();
        _ = builder.Services.AddSingleton(fake);
        fake._app = builder.Build();
        _ = fake._app.MapGrpcService<FakeUtxoRpc>();
        await fake._app.StartAsync();
        fake.Endpoint = new Uri(fake._app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        return fake;
    }

    /// <summary>Invokes the test handler and counts every actual request.</summary>
    /// <param name="request">The parameter request.</param>
    /// <param name="context">The server-side call.</param>
    /// <returns>The test's response.</returns>
    public override Task<ReadParamsResponse> ReadParams(ReadParamsRequest request, ServerCallContext context)
    {
        _ = Interlocked.Increment(ref _requestCount);
        return Handler(request, context);
    }

    /// <summary>Stops and disposes the loopback server.</summary>
    /// <returns>A task for shutdown.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}
