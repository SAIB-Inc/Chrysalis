using System.Threading.Channels;

namespace Chrysalis.Network.Multiplexer;

/// <summary>The connection ended. InnerException holds its first cause, or null on a clean peer close.</summary>
public class ConnectionClosedException : ChannelClosedException
{
    /// <summary>Initializes a closed-connection exception.</summary>
    public ConnectionClosedException() { }
    /// <summary>Initializes an exception with a message.</summary>
    public ConnectionClosedException(string? message) : base(message) { }
    /// <summary>Initializes an exception with a message and the connection's cause.</summary>
    public ConnectionClosedException(string? message, Exception? innerException) : base(message, innerException) { }
    /// <summary>Initializes an exception with the connection's cause.</summary>
    public ConnectionClosedException(Exception? innerException) : base(innerException) { }
}
