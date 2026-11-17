using System.Buffers;
using Chrysalis.Codec.Extensions.Cardano.Core;
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
using Auxiliary = Chrysalis.Codec.Types.Cardano.Core.IAuxiliaryData;
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
        catch (Exception exception)
        {
            throw Unusable(_methods.ReadParams, exception);
        }
    }

    /// <summary>Reads all outputs for distinct addresses, preserving their order.</summary>
    /// <param name="address">The addresses to query.</param>
    /// <param name="cancellationToken">Cancels every request.</param>
    /// <returns>All pages, or an exception without partial results.</returns>
    public async Task<List<ResolvedInput>> GetUtxosAsync(List<string> address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Count == 0) { return []; }
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
                catch (Exception exception) when (exception is FormatException or IndexOutOfRangeException)
                {
                    throw new ArgumentException("Invalid Bech32 address.", nameof(address), exception);
                }
            }
        }

        if (addresses.Count == 1) { return await SearchAsync(addresses[0], cancellationToken); }
        List<ResolvedInput> result = [];
        Task<List<ResolvedInput>>[] batch = new Task<List<ResolvedInput>>[Math.Min(5, addresses.Count)];
        for (int start = 0; start < addresses.Count; start += batch.Length)
        {
            int size = Math.Min(batch.Length, addresses.Count - start);
            for (int i = 0; i < size; i++)
            {
                batch[i] = SearchAsync(addresses[start + i], cancellationToken);
            }

            List<ResolvedInput>[] outputs = await Task.WhenAll(batch.AsSpan(0, size));
            int total = result.Count;
            for (int i = 0; i < outputs.Length; i++) { total = checked(total + outputs[i].Count); }
            _ = result.EnsureCapacity(total);
            for (int i = 0; i < outputs.Length; i++) { result.AddRange(outputs[i]); }
        }

        return result;
    }

    private async Task<List<ResolvedInput>> SearchAsync(ByteString address, CancellationToken cancellationToken)
    {
        SearchUtxosRequest request = new()
        {
            Predicate = new UtxoPredicate { Match = new AnyUtxoPattern { Cardano = new Spec.TxOutputPattern { Address = new Spec.AddressPattern { ExactAddress = address } } } }
        };
        List<ResolvedInput> result = [];
        while (true)
        {
            LeanUtxosResponse response = await UnaryAsync(_methods.SearchUtxos, request, cancellationToken);
            try
            {
                _ = result.EnsureCapacity(checked(result.Count + response.Items.Count));
                for (int i = 0; i < response.Items.Count; i++)
                {
                    result.Add(ToResolvedInput(response.Items[i]));
                }

                if (!response.HasNextToken || response.NextToken.Length == 0)
                {
                    return result;
                }

                if (request.HasStartToken && response.NextToken == request.StartToken)
                {
                    throw new InvalidDataException("The server repeated start_token.");
                }

                request.StartToken = response.NextToken;
            }
            catch (Exception exception)
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
            CborSerializer.Deserialize<ITransactionOutput>(item.NativeBytes.Memory));
    }

    /// <summary>Reads one output by its transaction hash and index.</summary>
    /// <param name="txHash">A 32-byte hash in hex.</param>
    /// <param name="index">The output index.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The matching output, or null if not found.</returns>
    public async Task<ResolvedInput?> GetUtxoByOutRefAsync(string txHash, ulong index, CancellationToken cancellationToken)
    {
        ByteString hash = ParseHash(txHash);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, uint.MaxValue);
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
        catch (Exception exception)
        {
            throw Unusable(_methods.ReadUtxos, exception);
        }
    }

    /// <summary>Reads metadata within the server's retained history.</summary>
    /// <param name="txHash">A 32-byte hash in hex.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The metadata, or null when absent or not found.</returns>
    public async Task<LedgerMetadata?> GetTransactionMetadataAsync(string txHash, CancellationToken cancellationToken)
    {
        ByteString hash = ParseHash(txHash);
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
            ReadOnlyMemory<byte>? auxiliary = AuxiliaryData(tx);
            return auxiliary is { } data ? CborSerializer.Deserialize<Auxiliary>(data).Metadata() : null;
        }
        catch (Exception exception)
        {
            throw Unusable(_methods.ReadTx, exception);
        }
    }

    internal static ReadOnlyMemory<byte>? AuxiliaryData(ReadOnlyMemory<byte> tx)
    {
        CborReader reader = new(tx.Span);
        reader.ReadBeginArray();
        bool indefinite = tx.Span[0] == 0x9f;
        int count = reader.ReadSize() switch
        {
            _ when indefinite => -1,
            2 => 2,
            3 => 3,
            4 => 4,
            _ => throw new InvalidDataException("Invalid transaction array length.")
        };
        _ = reader.ReadDataItem();
        _ = reader.ReadDataItem();
        ReadOnlyMemory<byte>? auxiliary = null;
        bool ended = indefinite && !reader.Buffer.IsEmpty && reader.Buffer[0] == 0xff;
        if (count != 2 && !ended)
        {
            if (reader.Buffer.IsEmpty) { throw new InvalidDataException("Truncated transaction array."); }
            bool validityFlag = reader.Buffer[0] is 0xf4 or 0xf5;
            if (validityFlag)
            {
                if (!indefinite && count != 4) { throw new InvalidDataException("A validity flag requires four transaction items."); }
                _ = reader.ReadDataItem();
            }
            else if (count == 4) { throw new InvalidDataException("Missing transaction validity flag."); }

            if (reader.Buffer.IsEmpty || reader.Buffer[0] == 0xff) { throw new InvalidDataException("Missing transaction auxiliary item."); }
            bool absent = reader.Buffer[0] == 0xf6;
            int start = tx.Length - reader.Buffer.Length;
            _ = reader.ReadDataItem();
            if (!absent) { auxiliary = tx[start..(tx.Length - reader.Buffer.Length)]; }
        }

        if (indefinite ? reader.Buffer.Length != 1 || reader.Buffer[0] != 0xff : !reader.Buffer.IsEmpty)
        {
            throw new InvalidDataException("Unterminated transaction array or trailing bytes.");
        }
        return auxiliary;
    }

    /// <summary>Submits a transaction once; acceptance does not mean it is on chain.</summary>
    /// <param name="tx">The signed transaction.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The server's 32-byte reference as lower-case hex.</returns>
    public async Task<string> SubmitTransactionAsync(ITransaction tx, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tx);
        SubmitTxResponse response = await UnaryAsync(_methods.SubmitTx, new SubmitTxRequest
        {
            Tx = new SubmitChainTx { Raw = UnsafeByteOperations.UnsafeWrap(CborSerializer.SerializeToMemory(tx)) }
        }, cancellationToken);
        try
        {
            if (response.Ref.Length != 32) { throw new InvalidDataException("The submitted transaction ref must be 32 bytes."); }
            return Convert.ToHexStringLower(response.Ref.Span);
        }
        catch (Exception exception)
        {
            throw Unusable(_methods.SubmitTx, exception);
        }
    }

    /// <summary>Waits for the server to report confirmation, bounded only by the caller's token.</summary>
    /// <param name="txHash">The transaction's 32-byte hash in hex.</param>
    /// <param name="cancellationToken">Bounds the wait, including for dropped transactions.</param>
    /// <returns>A task that completes when the server reports the transaction on chain.</returns>
    public async Task WaitForConfirmationAsync(string txHash, CancellationToken cancellationToken)
    {
        ByteString hash = ParseHash(txHash);
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

    private static ByteString ParseHash(string hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Length != 64) { throw new ArgumentException("Expected a 32-byte hash in hex.", nameof(hash)); }
        byte[] bytes = new byte[32];
        if (Convert.FromHexString(hash, bytes, out int consumed, out int written) != OperationStatus.Done || consumed != hash.Length || written != bytes.Length)
        {
            throw new ArgumentException("Expected a 32-byte hash in hex.", nameof(hash));
        }

        return UnsafeByteOperations.UnsafeWrap(bytes);
    }

    private static UtxoRpcException Unusable(IMethod method, Exception cause) => new($"{method.Name}: {cause.Message}", cause);

    /// <summary>Reads all outputs without a cancellation token.</summary>
    /// <param name="address">The addresses to query.</param>
    /// <returns>All outputs.</returns>
    public Task<List<ResolvedInput>> GetUtxosAsync(List<string> address) => GetUtxosAsync(address, CancellationToken.None);
    /// <summary>Reads parameters without a cancellation token.</summary>
    /// <returns>The current parameters.</returns>
    public Task<ProtocolParams> GetParametersAsync() => GetParametersAsync(CancellationToken.None);
    /// <summary>Submits once without a cancellation token.</summary>
    /// <param name="tx">The signed transaction.</param>
    /// <returns>The server reference.</returns>
    public Task<string> SubmitTransactionAsync(ITransaction tx) => SubmitTransactionAsync(tx, CancellationToken.None);
    /// <summary>Reads one output without a cancellation token.</summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <param name="index">The output index.</param>
    /// <returns>The matching output or null.</returns>
    public Task<ResolvedInput?> GetUtxoByOutRefAsync(string txHash, ulong index) => GetUtxoByOutRefAsync(txHash, index, CancellationToken.None);
    /// <summary>Reads metadata without a cancellation token.</summary>
    /// <param name="txHash">The transaction hash.</param>
    /// <returns>The metadata or null.</returns>
    public Task<LedgerMetadata?> GetTransactionMetadataAsync(string txHash) => GetTransactionMetadataAsync(txHash, CancellationToken.None);

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
        catch (RpcException exception)
        {
            throw Failure(method, exception, cancellationToken);
        }
    }

    private static Exception Failure(IMethod method, RpcException exception, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? new OperationCanceledException($"{method.Name} was cancelled.", exception, cancellationToken)
            : new UtxoRpcException($"{method.Name} failed: {exception.StatusCode}: {exception.Status.Detail}", exception.StatusCode, exception);

    /// <summary>Disposes the channel; repeated calls have no effect.</summary>
    public void Dispose() => _channel.Dispose();
}
