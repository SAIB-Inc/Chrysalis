using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.Common;
using Chrysalis.Network.Cbor.Handshake;

namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// Implementation of the Ouroboros LocalStateQuery mini-protocol for querying the Cardano node's ledger state.
/// Supports acquire/release semantics to pin a consistent ledger snapshot before querying.
/// </summary>
public class LocalStateQuery : IAsyncDisposable
{
    private readonly ChannelBuffer _buffer;
    private readonly N2CVersionContext? _versions;

    /// <summary>Initializes a standalone handler whose handshake/version remains the caller's responsibility.</summary>
    /// <param name="channel">The local-state-query channel.</param>
    /// <remarks>For compatibility, built-in requests are not version-checked without context. The explicit-minimum overload requires context.</remarks>
    public LocalStateQuery(AgentChannel channel) : this(channel, (N2CVersionContext?)null) { }

    /// <summary>Initializes a standalone handler bound to the version negotiated by its caller.</summary>
    /// <param name="channel">The local-state-query channel.</param>
    /// <param name="negotiatedVersion">The real negotiated N2C wire version.</param>
    public LocalStateQuery(AgentChannel channel, N2CVersion negotiatedVersion) : this(channel, new N2CVersionContext(negotiatedVersion)) { }

    internal LocalStateQuery(AgentChannel channel, N2CVersionContext? versions)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _buffer = new ChannelBuffer(channel);
        _versions = versions;
    }

    /// <summary>Gets whether a ledger state snapshot has been acquired.</summary>
    public bool IsAcquired { get; private set; }

    /// <summary>
    /// Sends a query against the currently acquired ledger state and returns the result.
    /// </summary>
    /// <param name="query">The query request to execute.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The query result from the Cardano node.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no state has been acquired.</exception>
    public async Task<Result> QueryAsync(IQueryReq query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureBuiltInQueryVersion();
        if (!IsAcquired)
        {
            throw new InvalidOperationException("Must acquire state before querying. Call AcquireAsync first.");
        }

        await _buffer.SendFullMessageAsync(QueryRequest.New(query), cancellationToken).ConfigureAwait(false);
        return await _buffer.ReceiveFullMessageAsync<Result>(cancellationToken).ConfigureAwait(false);
    }

    // Shared with the fresh-state helpers, before they can release or acquire.
    internal void EnsureBuiltInQueryVersion() => _versions?.EnsureMinimum(32784, nameof(QueryAsync));

    // The custom-minimum path has the same one-state-machine send/receive body,
    // without calling the legacy overload's V16 guard. Keep the legacy overload's
    // guards inside its own async body to preserve its Task exception boundary.
    private async Task<Result> QueryCoreAsync(IQueryReq query, CancellationToken cancellationToken)
    {
        if (!IsAcquired)
        {
            throw new InvalidOperationException("Must acquire state before querying. Call AcquireAsync first.");
        }

        await _buffer.SendFullMessageAsync(QueryRequest.New(query), cancellationToken).ConfigureAwait(false);
        return await _buffer.ReceiveFullMessageAsync<Result>(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Executes a custom raw query only after checking its caller-declared minimum version.</summary>
    /// <param name="query">The raw request; custom version requirements are the caller's responsibility.</param>
    /// <param name="minimumVersion">The actual minimum N2C wire version required by this custom query.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The asynchronous send/receive operation, with one state machine and no wrapper async stage.</returns>
    /// <remarks>Argument checks and the minimum-version guard throw synchronously. Acquired-state and send/receive failures are delivered through the returned Task.</remarks>
    /// <exception cref="ArgumentNullException">A query or minimum version is null; thrown synchronously.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The minimum is outside the N2C wire family; thrown synchronously.</exception>
    /// <exception cref="InvalidOperationException">No negotiated context is available (synchronously), or no ledger state is acquired (through the returned Task).</exception>
    /// <exception cref="N2CVersionNotSupportedException">The negotiated version is below the requested minimum; thrown synchronously before any query is written.</exception>
    public Task<Result> QueryAsync(IQueryReq query, N2CVersion minimumVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(minimumVersion);
        if (_versions is null)
        {
            throw new InvalidOperationException("An explicit query minimum requires a negotiated-version context.");
        }
        _versions.EnsureMinimum(minimumVersion.Value, nameof(QueryAsync));
        return QueryCoreAsync(query, cancellationToken);
    }

    /// <summary>
    /// Acquires a ledger state snapshot at the specified point, or at the volatile tip if null.
    /// </summary>
    /// <param name="point">The chain point to acquire state at, or null for the volatile tip.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown if the node fails to acquire state at the requested point.</exception>
    public async Task AcquireAsync(Point? point, CancellationToken cancellationToken)
    {
        await _buffer.SendFullMessageAsync(AcquireTypes.Default(point), cancellationToken).ConfigureAwait(false);
        LocalStateQueryMessage acquireResponse = await _buffer.ReceiveFullMessageAsync<LocalStateQueryMessage>(cancellationToken).ConfigureAwait(false);

        if (acquireResponse is not Acquired)
        {
            throw new InvalidOperationException($"Failed to acquire state: {acquireResponse}");
        }

        IsAcquired = true;
    }

    /// <summary>
    /// Releases the currently acquired ledger state snapshot.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        if (IsAcquired)
        {
            await _buffer.SendFullMessageAsync(new Release(5), cancellationToken).ConfigureAwait(false);
            IsAcquired = false;
        }
    }


    /// <summary>
    /// Releases the acquired state if held, performing best-effort cleanup.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Best effort cleanup - channel may already be disposed
        }
        catch (InvalidOperationException)
        {
            // Best effort cleanup - protocol state may be invalid
        }

        GC.SuppressFinalize(this);
    }
}
