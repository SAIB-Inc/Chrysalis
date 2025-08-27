namespace Chrysalis.Wallet.CIPs.CIP8.Models;

/// <summary>
/// Base interface for all COSE message types
/// </summary>
public interface ICoseMessage
{
    /// <summary>
    /// Converts the COSE message to its CBOR byte representation
    /// </summary>
    byte[] ToCbor();
}