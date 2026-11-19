using System.Net;

namespace Chrysalis.Tx.Providers;

/// <summary>An HTTP provider rejected a request.</summary>
public sealed class ProviderStatusException : ProviderException
{
    /// <summary>Creates an exception without a message or status.</summary>
    public ProviderStatusException() { }

    /// <summary>Creates an exception with a message.</summary>
    public ProviderStatusException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and a cause.</summary>
    public ProviderStatusException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Creates an exception with an HTTP status and redacted diagnostics.</summary>
    public ProviderStatusException(string message, HttpStatusCode statusCode, TimeSpan? retryAfter, string detail)
        : base(message, (int)statusCode is 408 or 425 or 429 or (>= 500 and <= 599), detail) =>
        (StatusCode, RetryAfter) = (statusCode, retryAfter);

    /// <summary>The response's HTTP status code.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The delay advertised by the server, if any.</summary>
    public TimeSpan? RetryAfter { get; }
}
