using System.Buffers;
using System.Text;
using Chrysalis.Wallet.CIPs.CIP8.Models;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Utils;
using Dahomey.Cbor.Serialization;

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

    /// <summary>Sets the payload as raw bytes.</summary>
    public CoseSign1Builder WithPayload(byte[] payload)
    {
        _payload = payload ?? throw new ArgumentNullException(nameof(payload));
        return this;
    }

    /// <summary>Sets the payload as a UTF-8 string.</summary>
    public CoseSign1Builder WithPayload(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _payload = Encoding.UTF8.GetBytes(payload);
        return this;
    }

    /// <summary>Enables Blake2b-224 hashing of the payload before signing.</summary>
    public CoseSign1Builder HashPayload()
    {
        _hashPayload = true;
        return this;
    }

    /// <summary>Sets the external additional authenticated data.</summary>
    public CoseSign1Builder WithExternalAad(byte[] externalAad)
    {
        _externalAad = externalAad ?? throw new ArgumentNullException(nameof(externalAad));
        return this;
    }

    /// <summary>Sets the address to include in protected headers.</summary>
    public CoseSign1Builder WithAddress(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);
        _address = address;
        return this;
    }

    /// <summary>Sets the signing algorithm identifier.</summary>
    public CoseSign1Builder WithAlgorithm(int algorithmId = AlgorithmId.EdDSA)
    {
        _ = algorithmId;
        return this;
    }

    /// <summary>Marks the payload as detached (external).</summary>
    public CoseSign1Builder WithDetachedPayload()
    {
        _isPayloadExternal = true;
        return this;
    }

    /// <summary>Builds the COSE_Sign1 message using the provided signing key.</summary>
    public CoseSign1 Build(PrivateKey signingKey)
    {
        ArgumentNullException.ThrowIfNull(signingKey);

        byte[] payload = _hashPayload ? HashUtil.Blake2b224(_payload) : _payload;

        byte[] protectedHeaderBytes;
        if (_address != null || _protectedHeaders.Count > 0)
        {
            ArrayBufferWriter<byte> output = new();
            CborWriter protectedWriter = new(output);
            protectedWriter.WriteBeginMap(_address != null ? _protectedHeaders.Count + 2 : _protectedHeaders.Count + 1);

            protectedWriter.WriteInt32(1); // algorithm label
            protectedWriter.WriteInt32(-8); // EdDSA

            if (_address != null)
            {
                protectedWriter.WriteInt32(-8); // address label
                protectedWriter.WriteByteString(_address.ToBytes());
            }

            foreach (KeyValuePair<int, byte[]> header in _protectedHeaders)
            {
                protectedWriter.WriteInt32(header.Key);
                protectedWriter.WriteByteString(header.Value);
            }

            int mapSize = _address != null ? _protectedHeaders.Count + 2 : _protectedHeaders.Count + 1;
            protectedWriter.WriteEndMap(mapSize);
            protectedHeaderBytes = output.WrittenSpan.ToArray();
        }
        else
        {
            protectedHeaderBytes = [];
        }

        HeaderMap unprotectedHeaderMap = HeaderMap.WithHashed(_hashPayload);

        SigStructure sigStructure = new(
            Context: SigContext.Signature1,
            BodyProtected: protectedHeaderBytes,
            SignProtected: [],
            ExternalAad: _externalAad ?? [],
            Payload: payload
        );

        byte[] sigBytes = sigStructure.ToCbor();
        byte[] signature = signingKey.Sign(sigBytes);

        return new CoseSign1(
            ProtectedHeaders: protectedHeaderBytes,
            UnprotectedHeaders: unprotectedHeaderMap,
            Payload: _isPayloadExternal ? null : payload,
            Signature: signature
        );
    }
}
