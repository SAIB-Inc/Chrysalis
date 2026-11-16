using Chrysalis.Codec.Serialization;
using Chrysalis.Network.Cbor.Handshake;
using Chrysalis.Network.Core;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// Implementation of the Ouroboros Handshake mini-protocol for negotiating protocol versions with a Cardano node.
/// </summary>
public class Handshake(AgentChannel channel) : IMiniProtocol
{
    /// <summary>
    /// Gets the protocol type identifier.
    /// </summary>
    public static ProtocolType ProtocolType => ProtocolType.Handshake;

    private readonly ChannelBuffer _buffer = new(channel);

    /// <summary>
    /// Gets whether the protocol is done.
    /// </summary>
    public bool IsDone { get; private set; }

    /// <summary>
    /// Sends a version proposal and receives the handshake response.
    /// </summary>
    /// <param name="propose">The version proposal message.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The context-free decoded reply union, including raw refusal replies.</returns>
    /// <remarks>
    /// An empty query-reply version table has no family-bearing keys and is ambiguous between N2N and N2C.
    /// This raw API returns the union alternative chosen by the decoder without interpreting the proposal's family.
    /// Use the generic overload to interpret an empty query reply using that proposal context.
    /// </remarks>
    public async Task<HandshakeMessage> SendAsync(ProposeVersions propose, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(propose);
        await _buffer.SendFullMessageAsync<HandshakeMessage>(propose, cancellationToken).ConfigureAwait(false);
        HandshakeMessage response = await _buffer.ReceiveFullMessageAsync<HandshakeMessage>(cancellationToken).ConfigureAwait(false);
        IsDone = true;
        return response;
    }

    /// <summary>
    /// Sends a version proposal and receives the handshake response, deserializing to the specified type.
    /// </summary>
    /// <typeparam name="T">The expected handshake response type (e.g., N2NAcceptVersion or N2CAcceptVersion).</typeparam>
    /// <param name="propose">The version proposal message.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The decoded accept or query reply of the requested type.</returns>
    /// <remarks>Empty query-reply tables use the proposed version table's family, never the requested type, to resolve their wire ambiguity.</remarks>
    /// <exception cref="HandshakeRefusedException">The peer supplied a decoded refusal reason.</exception>
    public async Task<T> SendAsync<T>(ProposeVersions propose, CancellationToken cancellationToken) where T : HandshakeMessage
    {
        ArgumentNullException.ThrowIfNull(propose);
        await _buffer.SendFullMessageAsync<HandshakeMessage>(propose, cancellationToken).ConfigureAwait(false);
        HandshakeMessage response = await _buffer.ReceiveFullMessageAsync<HandshakeMessage>(cancellationToken).ConfigureAwait(false);
        IsDone = true;
        if (response is Refuse refused)
        {
            throw new HandshakeRefusedException(refused.Reason);
        }
        // Empty maps carry no family discriminator. Only the proposal supplies that
        // context; nonempty replies keep the union's existing wire-family validation.
        response = (propose.VersionTable, response) switch
        {
            (N2CVersionTable, N2NQueryReply { VersionTable.Value.Count: 0 }) => CborSerializer.Deserialize<N2CQueryReply>(response.Raw),
            (N2NVersionTable, N2CQueryReply { VersionTable.Value.Count: 0 }) => CborSerializer.Deserialize<N2NQueryReply>(response.Raw),
            _ => response
        };
        return response is T expected ? expected
            : throw new InvalidOperationException($"Expected {typeof(T).Name}, received {response.GetType().Name} during handshake.");
    }
}
