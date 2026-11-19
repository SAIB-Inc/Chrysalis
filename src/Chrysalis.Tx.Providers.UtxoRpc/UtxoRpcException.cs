using Grpc.Core;

namespace Chrysalis.Tx.Providers;

/// <summary>A failed UTxO RPC call or an unusable server response.</summary>
public sealed class UtxoRpcException : ProviderException
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
    /// <param name="message">The operation and status.</param>
    /// <param name="statusCode">The gRPC status.</param>
    /// <param name="detail">The redacted server detail.</param>
    public UtxoRpcException(string message, StatusCode statusCode, string detail)
        : base(message, statusCode is Grpc.Core.StatusCode.Unavailable or Grpc.Core.StatusCode.DeadlineExceeded
            or Grpc.Core.StatusCode.ResourceExhausted or Grpc.Core.StatusCode.Aborted, detail) =>
        StatusCode = statusCode;

    /// <summary>Gets the gRPC status, or null when a successful call returned unusable data.</summary>
    public StatusCode? StatusCode { get; }
}
