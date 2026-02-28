using Chrysalis.Cbor.Types;

namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// COSE algorithm identifiers
/// </summary>
public static class AlgorithmId
{
    /// <summary>
    /// Pure EdDSA - the algorithm used for Cardano addresses
    /// </summary>
    public const int EdDSA = -8;

    /// <summary>
    /// ChaCha20/Poly1305 with 256-bit key, 128-bit tag (for future encryption support)
    /// </summary>
    public const int ChaCha20Poly1305 = 24;
}

/// <summary>
/// COSE key type identifiers
/// </summary>
public static class KeyType
{
    /// <summary>
    /// Octet Key Pair (used for Ed25519)
    /// </summary>
    public const int OKP = 1;

    /// <summary>
    /// 2-coordinate Elliptic Curve
    /// </summary>
    public const int EC2 = 2;

    /// <summary>
    /// Symmetric keys
    /// </summary>
    public const int Symmetric = 4;
}

/// <summary>
/// Signature contexts for SigStructure
/// </summary>
public static class SigContext
{
    /// <summary>
    /// Context for full COSE_Sign messages with multiple signers.
    /// </summary>
    public const string Signature = "Signature";

    /// <summary>
    /// Context for COSE_Sign1 messages with a single signer.
    /// </summary>
    public const string Signature1 = "Signature1";

    /// <summary>
    /// Context for counter-signature operations.
    /// </summary>
    public const string CounterSignature = "CounterSignature";
}

/// <summary>
/// Common COSE header labels
/// </summary>
public static class HeaderLabels
{
    /// <summary>
    /// Algorithm identifier label (COSE standard).
    /// </summary>
    public const int Algorithm = 1;

    /// <summary>
    /// Critical headers label (COSE standard).
    /// </summary>
    public const int Criticality = 2;

    /// <summary>
    /// Content type label (COSE standard).
    /// </summary>
    public const int ContentType = 3;

    /// <summary>
    /// Key identifier label (COSE standard).
    /// </summary>
    public const int KeyId = 4;

    /// <summary>
    /// Initialization vector label (COSE standard).
    /// </summary>
    public const int IV = 5;

    /// <summary>
    /// Partial initialization vector label (COSE standard).
    /// </summary>
    public const int PartialIV = 6;

    /// <summary>
    /// Counter signature label (COSE standard).
    /// </summary>
    public const int CounterSignature = 7;

    /// <summary>
    /// CIP-8 custom label for address binding.
    /// </summary>
    public const string Address = "address";

    /// <summary>
    /// CIP-8 custom label indicating whether payload is hashed.
    /// </summary>
    public const string Hashed = "hashed";
}

/// <summary>
/// Common COSE header labels as CborLabel instances for type-safe usage
/// </summary>
public static class CoseHeaders
{
    /// <summary>
    /// Algorithm identifier as a CborLabel.
    /// </summary>
    public static readonly CborLabel Algorithm = 1;

    /// <summary>
    /// Critical headers as a CborLabel.
    /// </summary>
    public static readonly CborLabel Criticality = 2;

    /// <summary>
    /// Content type as a CborLabel.
    /// </summary>
    public static readonly CborLabel ContentType = 3;

    /// <summary>
    /// Key identifier as a CborLabel.
    /// </summary>
    public static readonly CborLabel KeyId = 4;

    /// <summary>
    /// Initialization vector as a CborLabel.
    /// </summary>
    public static readonly CborLabel IV = 5;

    /// <summary>
    /// Partial initialization vector as a CborLabel.
    /// </summary>
    public static readonly CborLabel PartialIV = 6;

    /// <summary>
    /// Counter signature as a CborLabel.
    /// </summary>
    public static readonly CborLabel CounterSignature = 7;

    /// <summary>
    /// CIP-8 address label as a CborLabel.
    /// </summary>
    public static readonly CborLabel Address = "address";

    /// <summary>
    /// CIP-8 hashed label as a CborLabel.
    /// </summary>
    public static readonly CborLabel Hashed = "hashed";
}
