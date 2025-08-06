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
    public const string Signature = "Signature";
    public const string Signature1 = "Signature1";
    public const string CounterSignature = "CounterSignature";
}

/// <summary>
/// Common COSE header labels
/// </summary>
public static class HeaderLabels
{
    // Standard COSE labels (integers)
    public const int Algorithm = 1;
    public const int Criticality = 2;
    public const int ContentType = 3;
    public const int KeyId = 4;
    public const int IV = 5;
    public const int PartialIV = 6;
    public const int CounterSignature = 7;
    
    // CIP-8 custom labels (strings)
    public const string Address = "address";
    public const string Hashed = "hashed";
}