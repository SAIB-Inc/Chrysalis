using Chrysalis.Network.Core;

namespace Chrysalis.Network.Multiplexer;

/// <summary>
/// A reply the peer owed did not arrive within the connection's time limit, and the connection has ended.
/// It is the <see cref="Exception.InnerException"/> of the <see cref="ConnectionClosedException"/> that the connection's calls throw.
/// </summary>
public sealed class ReplyTimeoutException : TimeoutException
{
    /// <summary>Initializes a reply-timeout exception.</summary>
    public ReplyTimeoutException() { }
    /// <summary>Initializes an exception with a message.</summary>
    public ReplyTimeoutException(string? message) : base(message) { }
    /// <summary>Initializes an exception with a message and an inner exception.</summary>
    public ReplyTimeoutException(string? message, Exception? innerException) : base(message, innerException) { }
    /// <summary>Initializes an exception for the protocol whose reply was owed and the limit that passed.</summary>
    public ReplyTimeoutException(ProtocolType protocol, TimeSpan limit) : base($"The peer sent no {protocol} reply within {limit}.")
    {
        Protocol = protocol;
        Limit = limit;
    }

    /// <summary>Gets the mini-protocol whose reply was owed.</summary>
    public ProtocolType Protocol { get; }
    /// <summary>Gets the limit that passed.</summary>
    public TimeSpan Limit { get; }
}
