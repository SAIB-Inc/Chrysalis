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
    private KeepAliveState _state = KeepAliveState.Client;
    private uint _pendingCookie;

    public KeepAliveState State => _state;
    public bool IsDone => _state == KeepAliveState.Done;
    public bool HasAgency => _state == KeepAliveState.Client;

    public async Task SendKeepAliveRequestAsync(CancellationToken cancellationToken)
    {
        EnsureAgency();
        uint cookie = GenerateCookie();
        await _buffer.SendFullMessageAsync(KeepAliveMessages.KeepAlive(cookie), cancellationToken);
        _pendingCookie = cookie;
        _state = KeepAliveState.Server;
    }

    public async Task ReceiveKeepAliveResponseAsync(CancellationToken cancellationToken)
    {
        EnsureAwaitingResponse();
        KeepAliveMessage message = await _buffer.ReceiveFullMessageAsync<KeepAliveMessage>(cancellationToken);

        if (message is not MessageKeepAliveResponse response)
        {
            throw new InvalidOperationException($"Invalid keepalive response: {message}");
        }

        EnsureCookieInRange(response.Cookie);

        if (response.Cookie != _pendingCookie)
        {
            throw new InvalidOperationException("Keepalive cookie mismatch.");
        }

        _state = KeepAliveState.Client;
    }

    public async Task KeepAliveRoundtripAsync(CancellationToken cancellationToken)
    {
        await SendKeepAliveRequestAsync(cancellationToken);
        await ReceiveKeepAliveResponseAsync(cancellationToken);
    }

    public async Task SendDoneAsync(CancellationToken cancellationToken)
    {
        EnsureAgency();
        await _buffer.SendFullMessageAsync(KeepAliveMessages.Done(), cancellationToken);
        _state = KeepAliveState.Done;
    }

    private void EnsureAgency()
    {
        if (_state != KeepAliveState.Client)
        {
            throw new InvalidOperationException($"Cannot send keepalive in state {_state}");
        }
    }

    private void EnsureAwaitingResponse()
    {
        if (_state != KeepAliveState.Server)
        {
            throw new InvalidOperationException($"Cannot receive keepalive response in state {_state}");
        }
    }

    private static uint GenerateCookie() => (uint)Random.Shared.Next(0, ushort.MaxValue + 1);

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
    private KeepAliveState _state = KeepAliveState.Client;
    private uint _pendingCookie;

    public KeepAliveState State => _state;
    public bool IsDone => _state == KeepAliveState.Done;
    public bool HasAgency => _state == KeepAliveState.Server;

    public async Task ReceiveKeepAliveRequestAsync(CancellationToken cancellationToken)
    {
        EnsureAwaitingRequest();
        KeepAliveMessage message = await _buffer.ReceiveFullMessageAsync<KeepAliveMessage>(cancellationToken);

        switch (message)
        {
            case MessageKeepAlive keepAlive:
                EnsureCookieInRange(keepAlive.Cookie);
                _pendingCookie = keepAlive.Cookie;
                _state = KeepAliveState.Server;
                break;
            case MessageDone:
                _state = KeepAliveState.Done;
                break;
            default:
                throw new InvalidOperationException($"Invalid keepalive request: {message}");
        }
    }

    public async Task SendKeepAliveResponseAsync(CancellationToken cancellationToken)
    {
        EnsureAgency();
        await _buffer.SendFullMessageAsync(KeepAliveMessages.KeepAliveResponse(_pendingCookie), cancellationToken);
        _state = KeepAliveState.Client;
    }

    public async Task KeepAliveRoundtripAsync(CancellationToken cancellationToken)
    {
        await ReceiveKeepAliveRequestAsync(cancellationToken);
        if (_state == KeepAliveState.Server)
        {
            await SendKeepAliveResponseAsync(cancellationToken);
        }
    }

    private void EnsureAgency()
    {
        if (_state != KeepAliveState.Server)
        {
            throw new InvalidOperationException($"Cannot send keepalive response in state {_state}");
        }
    }

    private void EnsureAwaitingRequest()
    {
        if (_state != KeepAliveState.Client)
        {
            throw new InvalidOperationException($"Cannot receive keepalive request in state {_state}");
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
