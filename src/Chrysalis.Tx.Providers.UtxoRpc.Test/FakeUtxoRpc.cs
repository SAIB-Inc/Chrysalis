using System.Net;
using System.Collections.Concurrent;
using Google.Protobuf;
using Utxorpc.V1Beta.Submit;
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

    internal ConcurrentQueue<IMessage> Requests { get; } = new();
    internal Func<SearchUtxosRequest, ServerCallContext, Task<SearchUtxosResponse>> SearchHandler { get; set; } = static (_, _) => Task.FromResult(new SearchUtxosResponse());
    internal Func<ReadUtxosRequest, ServerCallContext, Task<ReadUtxosResponse>> UtxosHandler { get; set; } = static (_, _) => Task.FromResult(new ReadUtxosResponse());
    internal Func<ReadTxRequest, ServerCallContext, Task<ReadTxResponse>> TxHandler { get; set; } = static (_, _) => Task.FromResult(new ReadTxResponse());
    internal FakeSubmitUtxoRpc Submit { get; }

    private FakeUtxoRpc() => Submit = new FakeSubmitUtxoRpc(this);

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
        _ = builder.Services.AddSingleton(fake.Submit);
        fake._app = builder.Build();
        _ = fake._app.MapGrpcService<FakeUtxoRpc>();
        _ = fake._app.MapGrpcService<FakeSubmitUtxoRpc>();
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
        Record(request);
        return Handler(request, context);
    }

    internal void Record(IMessage request)
    {
        Requests.Enqueue(request);
        _ = Interlocked.Increment(ref _requestCount);
    }

    /// <inheritdoc />
    public override Task<SearchUtxosResponse> SearchUtxos(SearchUtxosRequest request, ServerCallContext context)
    {
        Record(request);
        return SearchHandler(request, context);
    }
    /// <inheritdoc />
    public override Task<ReadUtxosResponse> ReadUtxos(ReadUtxosRequest request, ServerCallContext context)
    {
        Record(request);
        return UtxosHandler(request, context);
    }
    /// <inheritdoc />
    public override Task<ReadTxResponse> ReadTx(ReadTxRequest request, ServerCallContext context)
    {
        Record(request);
        return TxHandler(request, context);
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

/// <summary>The submit service shares the fake's request log.</summary>
internal sealed class FakeSubmitUtxoRpc(FakeUtxoRpc owner) : SubmitService.SubmitServiceBase
{
    internal Func<SubmitTxRequest, ServerCallContext, Task<SubmitTxResponse>> Handler { get; set; } = static (_, _) => Task.FromResult(new SubmitTxResponse());
    /// <inheritdoc />
    public override Task<SubmitTxResponse> SubmitTx(SubmitTxRequest request, ServerCallContext context)
    {
        owner.Record(request);
        return Handler(request, context);
    }
}
