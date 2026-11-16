using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.MiniProtocols;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// Provides a high-level client for connecting to Cardano nodes.
/// </summary>
public sealed class NodeClient : IDisposable
{
    /// <summary>Gets or sets the network magic number used during the handshake (e.g., 764824073 for mainnet).</summary>
    public ulong NetworkMagic { get; set; } = 2;

    private readonly Plexer _plexer;
    private Task? _plexerTask;
    private readonly N2CVersionContext _versions = new();
    private int _started;

    /// <summary>Gets the accepted node-to-client version, or null before successful negotiation.</summary>
    public N2CVersion? NegotiatedVersion => _versions.NegotiatedVersion;

    /// <summary>
    /// Gets the Handshake protocol handler.
    /// </summary>
    public Handshake Handshake { get; private set; }

    /// <summary>
    /// Gets the LocalStateQuery protocol handler.
    /// </summary>
    public LocalStateQuery LocalStateQuery { get; private set; }

    /// <summary>
    /// Gets the ChainSync protocol handler.
    /// </summary>
    public ChainSync ChainSync { get; private set; }

    /// <summary>
    /// Gets the LocalTxSubmit protocol handler.
    /// </summary>
    public LocalTxSubmit LocalTxSubmit { get; private set; }

    /// <summary>
    /// Gets the LocalTxMonitor protocol handler.
    /// </summary>
    public LocalTxMonitor LocalTxMonitor { get; private set; }

    /// <summary>
    /// Initializes a new instance of the NodeClient class.
    /// </summary>
    /// <param name="plexer">The multiplexer for managing protocol channels.</param>
    private NodeClient(Plexer plexer)
    {
        _plexer = plexer ?? throw new ArgumentNullException(nameof(plexer));
        Handshake = new(_plexer.SubscribeClient(ProtocolType.Handshake));
        ChainSync = new(_plexer.SubscribeClient(ProtocolType.ClientChainSync));
        LocalTxSubmit = new(_plexer.SubscribeClient(ProtocolType.LocalTxSubmission));
        LocalStateQuery = new(_plexer.SubscribeClient(ProtocolType.LocalStateQuery), _versions);
        LocalTxMonitor = new(_plexer.SubscribeClient(ProtocolType.LocalTxMonitor), _versions);
    }

    /// <summary>
    /// Creates and connects a new NodeClient instance to a Cardano node.
    /// </summary>
    /// <param name="socketPath">The path to the node's Unix domain socket.</param>
    /// <param name="cancellationToken">A token to cancel the connection operation.</param>
    /// <returns>A connected NodeClient instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the connection fails.</exception>
    public static async Task<NodeClient> ConnectAsync(string socketPath, CancellationToken cancellationToken = default)
    {
        UnixBearer? unixBearer = null;
        try
        {
            unixBearer = await UnixBearer.CreateAsync(socketPath, cancellationToken).ConfigureAwait(false);
            NodeClient client = CreateFromBearer(unixBearer);
            unixBearer = null; // Ownership transferred
            return client;
        }
        finally
        {
            unixBearer?.Dispose();
        }
    }

    /// <summary>
    /// Creates and connects a new NodeClient instance to a Cardano node over TCP.
    /// </summary>
    /// <param name="host">The host to connect to.</param>
    /// <param name="port">The port to connect to.</param>
    /// <param name="cancellationToken">A token to cancel the connection operation.</param>
    /// <returns>A connected NodeClient instance.</returns>
    public static async Task<NodeClient> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        TcpBearer? tcpBearer = null;
        try
        {
            tcpBearer = await TcpBearer.CreateAsync(host, port, cancellationToken).ConfigureAwait(false);
            NodeClient client = CreateFromBearer(tcpBearer);
            tcpBearer = null; // Ownership transferred
            return client;
        }
        finally
        {
            tcpBearer?.Dispose();
        }
    }

    private static NodeClient CreateFromBearer(IBearer bearer)
    {
        Plexer plexer = new(bearer);
        try
        {
            NodeClient client = new(plexer);
            return client;
        }
        catch
        {
            plexer.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Starts the client and initializes protocol handlers.
    /// </summary>
    /// <param name="networkMagic">The network magic number for the handshake.</param>
    /// <exception cref="InvalidOperationException">Thrown if the client is already started.</exception>
    public Task StartAsync(ulong networkMagic = 2) => StartAsync(VersionTables.N2cV10AndAbove(networkMagic), CancellationToken.None);

    /// <summary>Starts this connection with an explicit version offer; one entry forces one version.</summary>
    /// <param name="versions">A nonempty, same-network N2C offer with query=false. Explicit untested versions do not change the default support claim.</param>
    /// <param name="cancellationToken">Cancels startup; it does not change the lifetime of an accepted connection.</param>
    /// <exception cref="ArgumentException">The offer is empty, duplicates a wire version, mixes networks or requests query mode.</exception>
    /// <exception cref="InvalidOperationException">The client was already started or the accept does not match its offer.</exception>
    /// <exception cref="HandshakeRefusedException">The node supplied a typed refusal.</exception>
    public async Task StartAsync(N2CVersionTable versions, CancellationToken cancellationToken = default)
    {
        N2CVersionTable offer = NormalizeOffer(versions);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The client has already started its handshake.");
        }
        NetworkMagic = offer.Value.First().Value.NetworkMagic;
        _plexerTask = _plexer.RunAsync(CancellationToken.None);
        try
        {
            N2CAcceptVersion accepted = await Handshake.SendAsync<N2CAcceptVersion>(HandshakeMessages.ProposeVersions(offer), cancellationToken).ConfigureAwait(false);
            if (!offer.Value.Keys.Any(version => version.Value == accepted.Version.Value)
                || accepted.VersionData.NetworkMagic != NetworkMagic || accepted.VersionData.Query is not false)
            {
                throw new InvalidOperationException("The node accepted a version or network datum outside the explicit handshake offer.");
            }
            _versions.SetNegotiatedVersion(accepted.Version);
        }
        catch
        {
            _plexer.Dispose();
            throw;
        }
    }

    private static N2CVersionTable NormalizeOffer(N2CVersionTable versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(versions.Value);
        if (versions.Value.Count == 0)
        {
            throw new ArgumentException("A handshake must offer at least one version.", nameof(versions));
        }
        N2CVersionData first = versions.Value.First().Value
            ?? throw new ArgumentException("Handshake version data cannot be null.", nameof(versions));
        ulong magic = first.NetworkMagic;
        Dictionary<N2CVersion, N2CVersionData> sorted = [];
        HashSet<int> seen = [];
        foreach (KeyValuePair<N2CVersion, N2CVersionData> entry in versions.Value.OrderBy(entry => entry.Key.Value))
        {
            N2CVersionContext.ValidateWireVersion(entry.Key, nameof(versions));
            if (entry.Value is null || !seen.Add(entry.Key.Value) || entry.Value.NetworkMagic != magic || entry.Value.Query is not false)
            {
                throw new ArgumentException("Handshake versions must be distinct, use one network magic and have query=false.", nameof(versions));
            }
            sorted.Add(new N2CVersion(entry.Key.Value), new N2CVersionData(magic, false));
        }
        return new N2CVersionTable(sorted);
    }

    /// <summary>
    /// Checks if the plexer (multiplexer/demultiplexer) is healthy and running.
    /// </summary>
    /// <returns>True if the plexer is running normally, false if it has stopped or faulted.</returns>
    /// <remarks>
    /// Task.IsCompleted returns true when the task is in RanToCompletion, Faulted, or Canceled state,
    /// so this single check covers all failure scenarios.
    /// </remarks>
    public bool IsPlexerHealthy() => _plexerTask is { IsCompleted: false };

    /// <summary>
    /// Gets the exception that caused the plexer to fail, if any.
    /// </summary>
    /// <returns>The base exception from the plexer task, or null if no exception occurred.</returns>
    public Exception? GetPlexerException() => _plexerTask?.Exception?.GetBaseException();

    /// <summary>
    /// Disposes the client and releases all resources.
    /// </summary>
    public void Dispose() => _plexer.Dispose();
}
