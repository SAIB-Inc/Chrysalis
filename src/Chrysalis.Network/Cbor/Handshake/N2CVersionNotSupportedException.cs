namespace Chrysalis.Network.Cbor.Handshake;

/// <summary>A version-dependent operation was rejected before writing because the negotiated version is too low.</summary>
public sealed class N2CVersionNotSupportedException : NotSupportedException
{
    /// <summary>Initializes an exception without negotiated metadata.</summary>
    public N2CVersionNotSupportedException() { }

    /// <summary>Initializes an exception with a caller-provided message.</summary>
    /// <param name="message">The diagnostic message.</param>
    public N2CVersionNotSupportedException(string? message) : base(message) { }

    /// <summary>Initializes an exception with a caller-provided message and cause.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public N2CVersionNotSupportedException(string? message, Exception? innerException) : base(message, innerException) { }

    /// <summary>Initializes the complete metadata for an SDK-rejected operation.</summary>
    /// <param name="operation">The operation that would have been sent.</param>
    /// <param name="requiredVersion">The minimum wire version.</param>
    /// <param name="negotiatedVersion">The version actually negotiated by this connection.</param>
    public N2CVersionNotSupportedException(string operation, N2CVersion requiredVersion, N2CVersion negotiatedVersion)
        : base(Describe(operation, requiredVersion, negotiatedVersion))
    {
        Operation = operation;
        RequiredVersion = requiredVersion;
        NegotiatedVersion = negotiatedVersion;
    }

    /// <summary>Gets the operation; null only for manually constructed exceptions without metadata.</summary>
    public string? Operation { get; }

    /// <summary>Gets the required version; always present on SDK-thrown version rejections.</summary>
    public N2CVersion? RequiredVersion { get; }

    /// <summary>Gets the negotiated version; always present on SDK-thrown version rejections.</summary>
    public N2CVersion? NegotiatedVersion { get; }

    private static string Describe(string operation, N2CVersion requiredVersion, N2CVersion negotiatedVersion)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation);
        ArgumentNullException.ThrowIfNull(requiredVersion);
        ArgumentNullException.ThrowIfNull(negotiatedVersion);
        return FormattableString.Invariant($"{operation} requires N2C wire version {requiredVersion.Value}, but this connection negotiated {negotiatedVersion.Value}.");
    }
}
