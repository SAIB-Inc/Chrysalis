using System.Security.Cryptography;
using Chrysalis.Network.Cbor.KeepAlive;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

/// <summary>
/// Represents the state machine states for the Ouroboros KeepAlive mini-protocol.
/// </summary>
public enum KeepAliveState
{
    /// <summary>The client has agency and can send a KeepAlive request or Done message.</summary>
    Client,
    /// <summary>The server has agency and must respond with a KeepAlive response.</summary>
    Server,
    /// <summary>The protocol has been terminated.</summary>
    Done
}

/// <summary>
/// Client-side implementation of the Ouroboros KeepAlive mini-protocol.
/// Sends periodic keep-alive requests with a random cookie and validates the echoed response.
/// </summary>
public sealed class KeepAliveClient(AgentChannel channel) : IMiniProtocol
{
    private readonly ChannelBuffer _buffer = new(channel);
    private uint _pendingCookie;

    /// <summary>Gets the current state of the KeepAlive protocol.</summary>
    public KeepAliveState State { get; private set; } = KeepAliveState.Client;

    /// <summary>Gets whether the protocol has been terminated.</summary>
    public bool IsDone => State == KeepAliveState.Done;

    /// <summary>Gets whether the client currently has agency to send messages.</summary>
    public bool HasAgency => State == KeepAliveState.Client;

    /// <summary>
    /// Sends a KeepAlive request with a randomly generated cookie to the Cardano node.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task SendKeepAliveRequestAsync(CancellationToken cancellationToken)
    {
        EnsureAgency();
        uint cookie = GenerateCookie();
        await _buffer.SendFullMessageAsync(KeepAliveMessages.KeepAlive(cookie), cancellationToken).ConfigureAwait(false);
        _pendingCookie = cookie;
        State = KeepAliveState.Server;
    }

    /// <summary>
    /// Receives and validates a KeepAlive response from the Cardano node, ensuring the cookie matches.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">Thrown if the response is invalid or the cookie does not match.</exception>
    public async Task ReceiveKeepAliveResponseAsync(CancellationToken cancellationToken)
    {
        EnsureAwaitingResponse();
        KeepAliveMessage message = await _buffer.ReceiveFullMessageAsync<KeepAliveMessage>(cancellationToken).ConfigureAwait(false);

        if (message is not MessageKeepAliveResponse response)
        {
            throw new InvalidOperationException($"Invalid keepalive response: {message}");
        }

        EnsureCookieInRange(response.Cookie);

        if (response.Cookie != _pendingCookie)
        {
            throw new InvalidOperationException("Keepalive cookie mismatch.");
        }

        State = KeepAliveState.Client;
    }

    /// <summary>
    /// Performs a complete KeepAlive roundtrip: sends a request and waits for the matching response.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task KeepAliveRoundtripAsync(CancellationToken cancellationToken)
    {
        await SendKeepAliveRequestAsync(cancellationToken).ConfigureAwait(false);
        await ReceiveKeepAliveResponseAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a Done message to terminate the KeepAlive protocol.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task SendDoneAsync(CancellationToken cancellationToken)
    {
        EnsureAgency();
        await _buffer.SendFullMessageAsync(KeepAliveMessages.Done(), cancellationToken).ConfigureAwait(false);
        State = KeepAliveState.Done;
    }

    private void EnsureAgency()
    {
        if (State != KeepAliveState.Client)
        {
            throw new InvalidOperationException($"Cannot send keepalive in state {State}");
        }
    }

    private void EnsureAwaitingResponse()
    {
        if (State != KeepAliveState.Server)
        {
            throw new InvalidOperationException($"Cannot receive keepalive response in state {State}");
        }
    }

    private static uint GenerateCookie()
    {
        return (uint)RandomNumberGenerator.GetInt32(0, ushort.MaxValue + 1);
    }

    private static void EnsureCookieInRange(uint cookie)
    {
        if (cookie > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Keepalive cookie out of range: {cookie}");
        }
    }
}

/// <summary>
/// Server-side implementation of the Ouroboros KeepAlive mini-protocol.
/// Receives keep-alive requests and echoes the cookie back in a response.
/// </summary>
public sealed class KeepAliveServer(AgentChannel channel) : IMiniProtocol
{
    private readonly ChannelBuffer _buffer = new(channel);
    private uint _pendingCookie;

    /// <summary>Gets the current state of the KeepAlive protocol.</summary>
    public KeepAliveState State { get; private set; } = KeepAliveState.Client;

    /// <summary>Gets whether the protocol has been terminated.</summary>
    public bool IsDone => State == KeepAliveState.Done;

    /// <summary>Gets whether the server currently has agency to send messages.</summary>
    public bool HasAgency => State == KeepAliveState.Server;

    /// <summary>
    /// Receives a KeepAlive request or Done message from the client.
    /// If a Done message is received, transitions to the Done state.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task ReceiveKeepAliveRequestAsync(CancellationToken cancellationToken)
    {
        EnsureAwaitingRequest();
        KeepAliveMessage message = await _buffer.ReceiveFullMessageAsync<KeepAliveMessage>(cancellationToken).ConfigureAwait(false);

        switch (message)
        {
            case MessageKeepAlive keepAlive:
                EnsureCookieInRange(keepAlive.Cookie);
                _pendingCookie = keepAlive.Cookie;
                State = KeepAliveState.Server;
                break;
            case MessageDone:
                State = KeepAliveState.Done;
                break;
            default:
                throw new InvalidOperationException($"Invalid keepalive request: {message}");
        }
    }

    /// <summary>
    /// Sends a KeepAlive response echoing the cookie from the most recent request.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task SendKeepAliveResponseAsync(CancellationToken cancellationToken)
    {
        EnsureAgency();
        await _buffer.SendFullMessageAsync(KeepAliveMessages.KeepAliveResponse(_pendingCookie), cancellationToken).ConfigureAwait(false);
        State = KeepAliveState.Client;
    }

    /// <summary>
    /// Performs a complete server-side KeepAlive roundtrip: receives a request and sends the response.
    /// If the client sends Done instead, transitions to the Done state without sending a response.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task KeepAliveRoundtripAsync(CancellationToken cancellationToken)
    {
        await ReceiveKeepAliveRequestAsync(cancellationToken).ConfigureAwait(false);
        if (State == KeepAliveState.Server)
        {
            await SendKeepAliveResponseAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void EnsureAgency()
    {
        if (State != KeepAliveState.Server)
        {
            throw new InvalidOperationException($"Cannot send keepalive response in state {State}");
        }
    }

    private void EnsureAwaitingRequest()
    {
        if (State != KeepAliveState.Client)
        {
            throw new InvalidOperationException($"Cannot receive keepalive request in state {State}");
        }
    }

    private static void EnsureCookieInRange(uint cookie)
    {
        if (cookie > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Keepalive cookie out of range: {cookie}");
        }
    }
}
