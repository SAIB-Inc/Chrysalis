using Chrysalis.Wallet.CIPs.CIP8.Builders;
using Chrysalis.Wallet.CIPs.CIP8.Models;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Wallet.CIPs.CIP8.Signers;

/// <summary>
/// EdDSA (Ed25519) implementation of COSE signing for Cardano.
/// </summary>
public class EdDsaCoseSigner : ICoseSigner
{
    /// <inheritdoc/>
    public CoseSign1 BuildCoseSign1(
        byte[] payload,
        PrivateKey signingKey,
        byte[]? externalAad = null,
        byte[]? address = null,
        bool hashPayload = false)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(signingKey);

        CoseSign1Builder builder = new CoseSign1Builder()
            .WithPayload(payload)
            .WithAlgorithm(AlgorithmId.EdDSA);

        if (externalAad != null)
        {
            _ = builder.WithExternalAad(externalAad);
        }

        if (address != null)
        {
            // Create Address object from bytes to use in builder
            Address addr = Address.FromBytes(address);
            _ = builder.WithAddress(addr);
        }

        if (hashPayload)
        {
            _ = builder.HashPayload();
        }

        return builder.Build(signingKey);
    }

    /// <inheritdoc/>
    public bool VerifyCoseSign1(
        CoseSign1 coseSign1,
        PublicKey verificationKey,
        byte[]? externalAad = null,
        byte[]? payload = null)
    {
        ArgumentNullException.ThrowIfNull(coseSign1);
        ArgumentNullException.ThrowIfNull(verificationKey);

        try
        {
            // Get the payload - either from the message or provided externally
            byte[] actualPayload = payload ?? coseSign1.Payload ?? throw new ArgumentException("Payload is required for verification. Either include it in the message or provide it as a parameter.");

            // Check if payload was hashed by examining unprotected headers
            bool isHashed = false;

            // TODO: Check for "hashed" header in unprotected headers
            // For now, we'll need to handle this when we have proper custom header support

            // Apply hashing if needed
            if (isHashed)
            {
                actualPayload = HashUtil.Blake2b224(actualPayload);
            }

            // Reconstruct the SigStructure that was signed
            SigStructure sigStructure = new(
                Context: SigContext.Signature1,
                BodyProtected: coseSign1.ProtectedHeaders,
                SignProtected: [], // Empty for Signature1
                ExternalAad: externalAad ?? [],
                Payload: actualPayload
            );

            // Serialize the SigStructure to get the signed data
            byte[] signedData = sigStructure.ToCbor();

            // Verify the signature
            return verificationKey.Verify(signedData, coseSign1.Signature);
        }
        catch (ArgumentException)
        {
            // Re-throw argument exceptions (invalid inputs)
            throw;
        }
        catch (FormatException)
        {
            // Format issues during verification mean invalid signature
            return false;
        }
        catch (InvalidOperationException)
        {
            // Operation issues during verification mean invalid signature
            return false;
        }
    }
}
