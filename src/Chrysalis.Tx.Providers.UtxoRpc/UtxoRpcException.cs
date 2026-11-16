using Grpc.Core;

namespace Chrysalis.Tx.Providers;

/// <summary>A failed UTxO RPC call or an unusable server response.</summary>
public sealed class UtxoRpcException : Exception
{
    /// <summary>Initializes an exception without a message or status.</summary>
    public UtxoRpcException()
    {
    }

    /// <summary>Initializes an exception with a message.</summary>
    /// <param name="message">What failed.</param>
    public UtxoRpcException(string message) : base(message)
    {
    }

    /// <summary>Initializes an exception with its underlying cause.</summary>
    /// <param name="message">What failed.</param>
    /// <param name="innerException">The underlying cause.</param>
    public UtxoRpcException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>Initializes an exception for a failed gRPC call.</summary>
    /// <param name="message">The operation, status and server detail.</param>
    /// <param name="statusCode">The gRPC status.</param>
    /// <param name="innerException">The underlying call failure.</param>
    public UtxoRpcException(string message, StatusCode statusCode, Exception innerException) : base(message, innerException) =>
        StatusCode = statusCode;

    /// <summary>Gets the gRPC status, or null when a successful call returned unusable data.</summary>
    public StatusCode? StatusCode { get; }
}
