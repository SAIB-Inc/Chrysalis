namespace Chrysalis.Tx.Providers;

/// <summary>A provider failed to complete an operation or returned unusable data.</summary>
public class ProviderException : Exception
{
    /// <summary>Creates an exception without a message.</summary>
    public ProviderException() { }

    /// <summary>Creates an exception with a message.</summary>
    public ProviderException(string message) : base(message) { }

    /// <summary>Creates an exception with a message and a cause.</summary>
    public ProviderException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Creates a provider failure with redacted server diagnostics.</summary>
    public ProviderException(string message, bool isTransient, string detail = "") : base(message) =>
        (IsTransient, Detail) = (isTransient, detail);

    /// <summary>Whether a failure may clear on a later read. Do not use this to retry a submission.</summary>
    public bool IsTransient { get; }

    /// <summary>Redacted server diagnostics, or an empty string when not available.</summary>
    public string Detail { get; } = "";
}
