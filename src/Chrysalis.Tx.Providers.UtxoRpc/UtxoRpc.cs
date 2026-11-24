using System.Buffers;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers.Lean;
using Chrysalis.Wallet.Models.Addresses;
using Google.Protobuf;
using SAIB.Cbor.Serialization;
using Utxorpc.V1Beta.Submit;
using Spec = Utxorpc.V1Beta.Cardano;
using LedgerMetadata = Chrysalis.Codec.Types.Cardano.Core.Metadata;
using SubmitChainTx = Utxorpc.V1Beta.Submit.AnyChainTx;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Wallet.Models.Enums;
using Grpc.Core;
using Grpc.Net.Client;
using Utxorpc.V1Beta.Query;
using GrpcMetadata = Grpc.Core.Metadata;

namespace Chrysalis.Tx.Providers;

/// <summary>Reads Cardano data from a UTxO RPC endpoint.</summary>
public sealed class UtxoRpc : ICardanoDataProvider, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly CallInvoker _invoker;
    private readonly GrpcMetadata _headers = [];
    private readonly TimeSpan _timeout;
    private readonly string[] _secrets;
    private readonly UtxoRpcMethods _methods = UtxoRpcMethods.V1Beta;
    private int _disposed;

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

        _secrets = [.. ProviderHttp.SecretsOf(endpoint), .. _headers.SelectMany(header => ProviderHttp.SecretsOfHeader(header.Value))];
        NetworkType = networkType;
        _channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions { MaxReceiveMessageSize = null });
        _invoker = _channel.CreateCallInvoker();
    }

    /// <summary>Gets the Cardano network this provider uses.</summary>
    public NetworkType NetworkType { get; }

    /// <summary>Reads the current protocol parameters.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The parameters reported by the server, with missing message fields left unknown.</returns>
    public async Task<ProtocolParams> GetParametersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadParamsResponse response = await UnaryAsync(_methods.ReadParams, new ReadParamsRequest(), cancellationToken);
        try
        {
            return UtxoRpcParameters.Map(response.Values?.Cardano
                ?? throw new InvalidDataException("values.cardano is missing."));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw Unusable(_methods.ReadParams, exception);
        }
    }

    /// <summary>Reads all outputs for distinct addresses, preserving their order.</summary>
    /// <param name="address">The addresses to query.</param>
    /// <param name="cancellationToken">Cancels every request.</param>
    /// <returns>All pages, or an exception without partial results.</returns>
    public async Task<List<ResolvedInput>> GetUtxosAsync(List<string> address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        HashSet<string> seen = new(address.Count, StringComparer.Ordinal);
        List<ByteString> addresses = [with(address.Count)];
        foreach (string item in address)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (seen.Add(item))
            {
                try
                {
                    addresses.Add(UnsafeByteOperations.UnsafeWrap(Address.FromBech32(item).ToBytes()));
                }
                catch (Exception exception) when (exception is FormatException or IndexOutOfRangeException or ArgumentException)
                {
                    throw new ArgumentException("Invalid Bech32 address.", nameof(address));
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (addresses.Count == 0) { return []; }
        if (addresses.Count == 1) { return await SearchAsync(addresses[0], cancellationToken); }
        ResolvedInputSet merged = new(nameof(UtxoRpc), _methods.SearchUtxos.Name);
        Task<List<ResolvedInput>>[] batch = new Task<List<ResolvedInput>>[Math.Min(5, addresses.Count)];
        for (int start = 0; start < addresses.Count; start += batch.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int size = Math.Min(batch.Length, addresses.Count - start);
            for (int i = 0; i < size; i++) { batch[i] = SearchAsync(addresses[start + i], cancellationToken); }
            List<ResolvedInput>[] answers = await ProviderHttp.AwaitBatchAsync(Task.WhenAll(batch.AsSpan(0, size)), cancellationToken);
            for (int i = 0; i < answers.Length; i++)
            {
                string request = (start + i).ToString(System.Globalization.CultureInfo.InvariantCulture);
                foreach (ResolvedInput output in answers[i]) { merged.Add(request, output); }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return merged.ToList();
    }

    private async Task<List<ResolvedInput>> SearchAsync(ByteString address, CancellationToken cancellationToken)
    {
        SearchUtxosRequest request = new()
        {
            Predicate = new UtxoPredicate { Match = new AnyUtxoPattern { Cardano = new Spec.TxOutputPattern { Address = new Spec.AddressPattern { ExactAddress = address } } } }
        };
        ResolvedInputSet found = new(nameof(UtxoRpc), _methods.SearchUtxos.Name);
        HashSet<string> cursors = [with(StringComparer.Ordinal)];
        while (true)
        {
            LeanUtxosResponse response = await UnaryAsync(_methods.SearchUtxos, request, cancellationToken);
            try
            {
                bool more = response.HasNextToken && response.NextToken.Length != 0;
                if (more && !cursors.Add(response.NextToken)) { throw new InvalidDataException("The server repeated start_token."); }
                for (int i = 0; i < response.Items.Count; i++)
                {
                    found.Add("search", ToResolvedInput(response.Items[i]));
                }
                if (!more) { return found.ToList(); }
                request.StartToken = response.NextToken;
            }
            catch (Exception exception) when (exception is not (ProviderException or OutOfMemoryException))
            {
                throw Unusable(_methods.SearchUtxos, exception);
            }
        }
    }

    internal static ResolvedInput ToResolvedInput(LeanUtxo item)
    {
        if (item.NativeBytes.IsEmpty || item.TxoRef is null || item.TxoRef.Hash.Length != 32)
        {
            throw new InvalidDataException("An output needs native_bytes and a 32-byte txo_ref.hash.");
        }

        ArrayBufferWriter<byte> reference = new(40); // Array header + byte-string header + hash32 + uint32.
        CborWriter writer = new(reference);
        writer.WriteBeginArray(2);
        writer.WriteByteString(item.TxoRef.Hash.Span);
        writer.WriteUInt64(item.TxoRef.Index);
        writer.WriteEndArray(2);
        return new ResolvedInput(TransactionInput.Read(reference.WrittenMemory),
            ProviderValues.CheckedOutput(CborSerializer.Deserialize<ITransactionOutput>(item.NativeBytes.Memory)));
    }

    /// <summary>Reads one output by its transaction hash and index.</summary>
    /// <param name="txHash">A 32-byte hash in hex.</param>
    /// <param name="index">The output index.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The matching output, or null if not found.</returns>
    public async Task<ResolvedInput?> GetUtxoByOutRefAsync(string txHash, ulong index, CancellationToken cancellationToken = default)
    {
        ByteString hash = UnsafeByteOperations.UnsafeWrap(ProviderValues.TransactionHash(txHash));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, uint.MaxValue);
        cancellationToken.ThrowIfCancellationRequested();
        LeanUtxosResponse response;
        try
        {
            response = await UnaryAsync(_methods.ReadUtxos, new ReadUtxosRequest { Keys = { new TxoRef { Hash = hash, Index = (uint)index } } }, cancellationToken);
        }
        catch (UtxoRpcException exception) when (exception.StatusCode == StatusCode.NotFound)
        {
            return null;
        }

        try
        {
            for (int i = 0; i < response.Items.Count; i++)
            {
                LeanUtxo item = response.Items[i];
                ResolvedInput output = ToResolvedInput(item);
                if (item.TxoRef.Hash.Equals(hash) && item.TxoRef.Index == index)
                {
                    return output;
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw Unusable(_methods.ReadUtxos, exception);
        }
    }

    /// <summary>Reads metadata within the server's retained history.</summary>
    /// <param name="txHash">A 32-byte hash in hex.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The metadata, or null when absent or not found.</returns>
    public async Task<LedgerMetadata?> GetTransactionMetadataAsync(string txHash, CancellationToken cancellationToken = default)
    {
        ByteString hash = UnsafeByteOperations.UnsafeWrap(ProviderValues.TransactionHash(txHash));
        cancellationToken.ThrowIfCancellationRequested();
        LeanReadTxResponse response;
        try
        {
            response = await UnaryAsync(_methods.ReadTx, new ReadTxRequest { Hash = hash }, cancellationToken);
        }
        catch (UtxoRpcException exception) when (exception.StatusCode == StatusCode.NotFound)
        {
            return null;
        }

        try
        {
            ReadOnlyMemory<byte> tx = response.Tx?.NativeBytes.Memory ?? throw new InvalidDataException("tx.native_bytes is missing.");
            return ProviderValues.TransactionMetadata(tx);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw Unusable(_methods.ReadTx, exception);
        }
    }

    /// <summary>Submits a transaction once; acceptance does not mean it is on chain.</summary>
    /// <param name="tx">The signed transaction.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The server's 32-byte reference as lower-case hex.</returns>
    public async Task<string> SubmitTransactionAsync(ITransaction tx, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tx);
        cancellationToken.ThrowIfCancellationRequested();
        SubmitTxResponse response = await UnaryAsync(_methods.SubmitTx, new SubmitTxRequest
        {
            Tx = new SubmitChainTx { Raw = UnsafeByteOperations.UnsafeWrap(CborSerializer.SerializeToMemory(tx)) }
        }, cancellationToken);
        try
        {
            if (response.Ref.Length != 32) { throw new InvalidDataException("The submitted transaction ref must be 32 bytes."); }
            return Convert.ToHexStringLower(response.Ref.Span);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw Unusable(_methods.SubmitTx, exception);
        }
    }

    /// <summary>Waits for the server to report confirmation, bounded only by the caller's token.</summary>
    /// <param name="txHash">The transaction's 32-byte hash in hex.</param>
    /// <param name="cancellationToken">Bounds the wait, including for dropped transactions.</param>
    /// <returns>A task that completes when the server reports the transaction on chain.</returns>
    public async Task WaitForConfirmationAsync(string txHash, CancellationToken cancellationToken = default)
    {
        ByteString hash = UnsafeByteOperations.UnsafeWrap(ProviderValues.TransactionHash(txHash));
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        try
        {
            using AsyncServerStreamingCall<WaitForTxResponse> call = _invoker.AsyncServerStreamingCall(
                _methods.WaitForTx, null, new CallOptions(_headers, cancellationToken: cancellationToken),
                new WaitForTxRequest { Ref = { hash } });
            bool first = true;
            while (await call.ResponseStream.MoveNext(cancellationToken))
            {
                WaitForTxResponse message = call.ResponseStream.Current;
                if (!message.Ref.Equals(hash)) { continue; }
                if (message.Stage == Stage.Confirmed) { return; }
                if (first && message.Stage == Stage.Unspecified && await IsOnChainAsync(hash, cancellationToken)) { return; }
                first = false;
            }

            throw Unusable(_methods.WaitForTx, new InvalidDataException("The stream ended before confirmation."));
        }
        catch (RpcException exception)
        {
            throw Failure(_methods.WaitForTx, exception, cancellationToken);
        }
        catch (Exception exception) when (exception is not (ProviderException or OutOfMemoryException))
        {
            throw ProviderHttp.TransportFailure(nameof(UtxoRpc), _methods.WaitForTx.Name, exception, cancellationToken);
        }
    }

    private async Task<bool> IsOnChainAsync(ByteString hash, CancellationToken cancellationToken)
    {
        try
        {
            LeanReadTxResponse response = await UnaryAsync(_methods.ReadTx, new ReadTxRequest { Hash = hash }, cancellationToken);
            if (response.Tx is null || response.Tx.NativeBytes.IsEmpty)
            {
                throw Unusable(_methods.ReadTx, new InvalidDataException("tx.native_bytes is missing."));
            }
            return true;
        }
        catch (UtxoRpcException exception) when (exception.StatusCode == StatusCode.NotFound)
        {
            return false;
        }
    }

    private static UtxoRpcException Unusable(IMethod method, Exception cause) => new($"{method.Name}: {UnusableReason(cause)}");

    private static string UnusableReason(Exception cause) => cause is InvalidDataException
        ? cause.Message : $"unusable response ({cause.GetType().Name})";

    private async Task<TResponse> UnaryAsync<TRequest, TResponse>(Method<TRequest, TResponse> method, TRequest request, CancellationToken cancellationToken)
        where TRequest : class
        where TResponse : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        try
        {
            using AsyncUnaryCall<TResponse> call = _invoker.AsyncUnaryCall(method, null,
                new CallOptions(_headers, DateTime.UtcNow + _timeout, cancellationToken), request);
            TResponse response = await call.ResponseAsync;
            return response;
        }
        catch (RpcException exception)
        {
            throw Failure(method, exception, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Exception translated = ProviderHttp.TransportFailure(nameof(UtxoRpc), method.Name, exception, cancellationToken);
            throw ReferenceEquals(method, _methods.SubmitTx) && translated is ProviderException { IsTransient: true }
                ? new ProviderException($"{translated.Message}; the transaction may have been accepted", isTransient: true)
                : translated;
        }
    }

    private Exception Failure(IMethod method, RpcException exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) { return new OperationCanceledException($"{method.Name} was cancelled.", cancellationToken); }
        string message = $"{method.Name} failed: {exception.StatusCode}";
        if (ReferenceEquals(method, _methods.SubmitTx) && exception.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
        {
            message += "; the transaction may have been accepted";
        }
        return new UtxoRpcException(message, exception.StatusCode, ProviderHttp.Redact(exception.Status.Detail, _secrets));
    }

    /// <summary>Disposes the channel; repeated calls have no effect.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) { _channel.Dispose(); }
    }
}
