using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Wallet.Models.Enums;
using Grpc.Core;
using Grpc.Net.Client;
using Utxorpc.V1Beta.Query;
using GrpcMetadata = Grpc.Core.Metadata;

namespace Chrysalis.Tx.Providers;

/// <summary>Reads Cardano data from a UTxO RPC endpoint.</summary>
public sealed class UtxoRpc : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly CallInvoker _invoker;
    private readonly GrpcMetadata _headers = [];
    private readonly TimeSpan _timeout;
    private readonly UtxoRpcMethods _methods = UtxoRpcMethods.V1Beta;

    /// <summary>Creates a provider with one reusable gRPC channel.</summary>
    /// <param name="endpoint">An absolute HTTP or HTTPS server URI.</param>
    /// <param name="networkType">The Cardano network to use.</param>
    /// <param name="headers">Optional metadata sent on every call.</param>
    /// <param name="timeout">The unary-call timeout; defaults to 30 seconds.</param>
    public UtxoRpc(Uri endpoint, NetworkType networkType, IReadOnlyDictionary<string, string>? headers = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("The endpoint must be an absolute HTTP or HTTPS URI.", nameof(endpoint));
        }

        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "The timeout must be positive.");
        }

        if (headers is not null)
        {
            foreach ((string name, string value) in headers)
            {
                _headers.Add(name, value);
            }
        }

        NetworkType = networkType;
        _channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions { MaxReceiveMessageSize = null });
        _invoker = _channel.CreateCallInvoker();
    }

    /// <summary>Gets the Cardano network this provider uses.</summary>
    public NetworkType NetworkType { get; }

    /// <summary>Reads the current protocol parameters.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The parameters reported by the server, with missing message fields left unknown.</returns>
    public async Task<ProtocolParams> GetParametersAsync(CancellationToken cancellationToken)
    {
        ReadParamsResponse response = await UnaryAsync(_methods.ReadParams, new ReadParamsRequest(), cancellationToken);
        try
        {
            return UtxoRpcParameters.Map(response.Values?.Cardano
                ?? throw new InvalidDataException("values.cardano is missing."));
        }
        catch (InvalidDataException exception)
        {
            throw new UtxoRpcException($"{_methods.ReadParams.Name}: {exception.Message}", exception);
        }
    }

    private async Task<TResponse> UnaryAsync<TRequest, TResponse>(Method<TRequest, TResponse> method, TRequest request, CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        try
        {
            using AsyncUnaryCall<TResponse> call = _invoker.AsyncUnaryCall(method, null,
                new CallOptions(_headers, DateTime.UtcNow + _timeout, cancellationToken), request);
            return await call.ResponseAsync;
        }
        catch (RpcException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException($"{method.Name} was cancelled.", exception, cancellationToken);
        }
        catch (RpcException exception)
        {
            throw new UtxoRpcException($"{method.Name} failed: {exception.StatusCode}: {exception.Status.Detail}", exception.StatusCode, exception);
        }
    }

    /// <summary>Disposes the channel; repeated calls have no effect.</summary>
    public void Dispose() => _channel.Dispose();
}
