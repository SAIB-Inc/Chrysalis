using System.Formats.Cbor;
using System.Text;
using Chrysalis.Wallet.CIPs.CIP8.Models;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;

namespace Chrysalis.Wallet.CIPs.CIP8.Builders;

/// <summary>
/// Builder for constructing COSE_Sign1 messages.
/// </summary>
public class CoseSign1Builder
{
    private byte[] _payload = [];
    private byte[]? _externalAad;
    private readonly Dictionary<int, byte[]> _protectedHeaders = [];
    private bool _isPayloadExternal;
    private bool _hashPayload;
    private Address? _address;

    /// <summary>
    /// Sets the message payload.
    /// </summary>
    /// <param name="payload">The payload bytes.</param>
    /// <returns>This builder instance for chaining.</returns>
    public CoseSign1Builder WithPayload(byte[] payload)
    {
        _payload = payload ?? throw new ArgumentNullException(nameof(payload));
        return this;
    }

    /// <summary>
    /// Sets the message payload from a string.
    /// </summary>
    /// <param name="payload">The payload string to encode as UTF-8.</param>
    /// <returns>This builder instance for chaining.</returns>
    public CoseSign1Builder WithPayload(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        _payload = Encoding.UTF8.GetBytes(payload);
        return this;
    }

    /// <summary>
    /// Configures the payload to be hashed with Blake2b224.
    /// </summary>
    /// <returns>This builder instance for chaining.</returns>
    public CoseSign1Builder HashPayload()
    {
        _hashPayload = true;
        return this;
    }

    /// <summary>
    /// Sets external additional authenticated data.
    /// </summary>
    /// <param name="externalAad">The external AAD bytes.</param>
    /// <returns>This builder instance for chaining.</returns>
    public CoseSign1Builder WithExternalAad(byte[] externalAad)
    {
        _externalAad = externalAad ?? throw new ArgumentNullException(nameof(externalAad));
        return this;
    }

    /// <summary>
    /// Binds the signature to a specific address.
    /// </summary>
    /// <param name="address">The Cardano address to bind to.</param>
    /// <returns>This builder instance for chaining.</returns>
    public CoseSign1Builder WithAddress(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        _address = address;
        return this;
    }

    /// <summary>
    /// Sets the algorithm identifier (defaults to EdDSA).
    /// </summary>
    /// <param name="algorithmId">The COSE algorithm identifier.</param>
    /// <returns>This builder instance for chaining.</returns>
    public CoseSign1Builder WithAlgorithm(int algorithmId = AlgorithmId.EdDSA)
    {
        // For CIP-8, algorithm is typically EdDSA and set in protected headers
        // We'll handle this in the Build method
        _ = algorithmId;
        return this;
    }

    /// <summary>
    /// Configures the payload to be detached (not included in the message).
    /// </summary>
    /// <returns>This builder instance for chaining.</returns>
    public CoseSign1Builder WithDetachedPayload()
    {
        _isPayloadExternal = true;
        return this;
    }

    /// <summary>
    /// Builds and signs the COSE_Sign1 message.
    /// </summary>
    /// <param name="signingKey">The private key used to sign the message.</param>
    /// <returns>A signed CoseSign1 message.</returns>
    public CoseSign1 Build(PrivateKey signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);

        // Apply hashing if requested
        byte[] payload = _hashPayload ? HashUtil.Blake2b224(_payload) : _payload;

        // Build protected headers
        byte[] protectedHeaderBytes;
        if (_address != null || _protectedHeaders.Count > 0)
        {
            CborWriter protectedWriter = new(CborConformanceMode.Strict);
            protectedWriter.WriteStartMap(_address != null ? _protectedHeaders.Count + 2 : _protectedHeaders.Count + 1);

            // Add algorithm (1 = EdDSA is -8)
            protectedWriter.WriteInt32(1); // algorithm label
            protectedWriter.WriteInt32(-8); // EdDSA

            // Add address if specified (-8 in COSE registry, but we use label 0x27 = 39 for address)
            if (_address != null)
            {
                protectedWriter.WriteInt32(-8); // address label (COSE registry)
                protectedWriter.WriteByteString(_address.ToBytes());
            }

            // Add any other protected headers
            foreach (KeyValuePair<int, byte[]> header in _protectedHeaders)
            {
                protectedWriter.WriteInt32(header.Key);
                protectedWriter.WriteByteString(header.Value);
            }

            protectedWriter.WriteEndMap();
            protectedHeaderBytes = protectedWriter.Encode();
        }
        else
        {
            // Empty protected headers
            protectedHeaderBytes = [];
        }

        // Build unprotected headers - always include "hashed" field
        HeaderMap unprotectedHeaderMap = HeaderMap.WithHashed(_hashPayload);

        // Build SigStructure for signing
        SigStructure sigStructure = new(
            Context: SigContext.Signature1,
            BodyProtected: protectedHeaderBytes,
            SignProtected: [], // Empty for Signature1
            ExternalAad: _externalAad ?? [],
            Payload: payload
        );

        // Sign the SigStructure
        byte[] sigBytes = sigStructure.ToCbor();
        byte[] signature = signingKey.Sign(sigBytes);

        // Build final CoseSign1
        return new CoseSign1(
            ProtectedHeaders: protectedHeaderBytes,
            UnprotectedHeaders: unprotectedHeaderMap,
            Payload: _isPayloadExternal ? null : payload,
            Signature: signature
        );
    }
}
