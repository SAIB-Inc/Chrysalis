using Chrysalis.Wallet.CIPs.CIP8.Models;
using Chrysalis.Wallet.Models.Keys;

namespace Chrysalis.Wallet.CIPs.CIP8.Signers;

/// <summary>
/// Interface for COSE message signing and verification
/// </summary>
public interface ICoseSigner
{
    /// <summary>
    /// Builds and signs a COSE_Sign1 message
    /// </summary>
    /// <param name="payload">The message payload</param>
    /// <param name="signingKey">The private key to sign with</param>
    /// <param name="externalAad">Optional external additional authenticated data</param>
    /// <param name="address">Optional address to bind the signature to</param>
    /// <param name="hashPayload">Whether to hash the payload with Blake2b224</param>
    /// <returns>A signed COSE_Sign1 message</returns>
    CoseSign1 BuildCoseSign1(
        byte[] payload,
        PrivateKey signingKey,
        byte[]? externalAad = null,
        byte[]? address = null,
        bool hashPayload = false);

    /// <summary>
    /// Verifies a COSE_Sign1 message signature.
    /// </summary>
    /// <param name="coseSign1">The COSE_Sign1 message to verify.</param>
    /// <param name="verificationKey">The public key to verify with.</param>
    /// <param name="externalAad">Optional external AAD (must match what was used during signing).</param>
    /// <param name="payload">Optional raw plaintext payload (never pre-hashed). It is hashed with Blake2b-224 when the unprotected <c>"hashed"</c> header is <see langword="true"/> and is always checked against any attached payload, whether hashed or not. A missing <c>"hashed"</c> header is treated as <see langword="false"/>; <c>"version"</c> is neither required nor emitted.</param>
    /// <remarks>Passing <see langword="null"/> for <paramref name="payload"/> authenticates only that <paramref name="verificationKey"/> signed the attached bytes and protected headers verbatim; it cannot distinguish a raw 28-byte attachment from a digest or prove key-to-address ownership.</remarks>
    /// <returns>True if the signature is valid, false otherwise.</returns>
    bool VerifyCoseSign1(
        CoseSign1 coseSign1,
        PublicKey verificationKey,
        byte[]? externalAad = null,
        byte[]? payload = null);
}