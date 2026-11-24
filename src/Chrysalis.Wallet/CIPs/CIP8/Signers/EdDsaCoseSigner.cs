using Chrysalis.Wallet.CIPs.CIP8.Builders;
using Chrysalis.Wallet.CIPs.CIP8.Extensions;
using Chrysalis.Wallet.CIPs.CIP8.Models;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Keys;

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
            SigStructure sigStructure = coseSign1.GetSigStructure(externalAad, payload);
            byte[] signedData = sigStructure.ToCbor();
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
