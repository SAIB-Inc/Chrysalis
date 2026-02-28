using System.Security.Cryptography;
using Chrysalis.Network.Cbor.KeepAlive;
using Chrysalis.Network.Multiplexer;

namespace Chrysalis.Network.MiniProtocols;

public enum KeepAliveState
{
    Client,
    Server,
    Done
}

public sealed class KeepAliveClient(AgentChannel channel) : IMiniProtocol
{
    private readonly ChannelBuffer _buffer = new(channel);
    private uint _pendingCookie;

    public KeepAliveState State { get; private set; } = KeepAliveState.Client;
    public bool IsDone => State == KeepAliveState.Done;
    public bool HasAgency => State == KeepAliveState.Client;

    public async Task SendKeepAliveRequestAsync(CancellationToken cancellationToken)
    {
        EnsureAgency();
        uint cookie = GenerateCookie();
        await _buffer.SendFullMessageAsync(KeepAliveMessages.KeepAlive(cookie), cancellationToken).ConfigureAwait(false);
        _pendingCookie = cookie;
        State = KeepAliveState.Server;
    }

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

    public async Task KeepAliveRoundtripAsync(CancellationToken cancellationToken)
    {
        await SendKeepAliveRequestAsync(cancellationToken).ConfigureAwait(false);
        await ReceiveKeepAliveResponseAsync(cancellationToken).ConfigureAwait(false);
    }

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

public sealed class KeepAliveServer(AgentChannel channel) : IMiniProtocol
{
    private readonly ChannelBuffer _buffer = new(channel);
    private uint _pendingCookie;

    public KeepAliveState State { get; private set; } = KeepAliveState.Client;
    public bool IsDone => State == KeepAliveState.Done;
    public bool HasAgency => State == KeepAliveState.Server;

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

    public async Task SendKeepAliveResponseAsync(CancellationToken cancellationToken)
    {
        EnsureAgency();
        await _buffer.SendFullMessageAsync(KeepAliveMessages.KeepAliveResponse(_pendingCookie), cancellationToken).ConfigureAwait(false);
        State = KeepAliveState.Client;
    }

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
