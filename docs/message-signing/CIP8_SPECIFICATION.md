# Chrysalis Message Signing Specification

## Overview

This specification defines the implementation of CIP-8 (Message Signing) for the Chrysalis ecosystem. The implementation provides a standardized way to sign and verify arbitrary messages using Cardano cryptographic keys, following the COSE (CBOR Object Signing and Encryption) standard as defined in RFC 8152.

**Status**: ✅ Implemented and verified against reference implementations

## Goals

1. **CIP-8 Compliance**: Full compatibility with the CIP-8 specification
2. **Integration**: Seamless integration with existing Chrysalis.Wallet module
3. **Type Safety**: Leverage C# type system for safe message construction
4. **Performance**: Efficient CBOR serialization using Chrysalis.Cbor
5. **Extensibility**: Support for future signing algorithms and message types
6. **Developer Experience**: Intuitive API with builder pattern support

## Architecture

### Namespace Structure

```
Chrysalis.Wallet/
└── CIPs/
    └── CIP8/
        ├── Models/
        │   ├── CoseSign1.cs
        │   ├── CoseSign.cs
        │   ├── CoseSignature.cs
        │   ├── Headers.cs
        │   ├── HeaderMap.cs
        │   ├── ProtectedHeaderMap.cs
        │   ├── SigStructure.cs
        │   ├── CoseEnums.cs
        │   ├── CoseKey.cs
        │   └── ICoseMessage.cs
        ├── Builders/
        │   ├── CoseSign1Builder.cs
        │   └── CoseSignBuilder.cs
        ├── Signers/
        │   ├── ICoseSigner.cs
        │   └── EdDsaCoseSigner.cs
        └── Extensions/
            ├── CoseMessageExtensions.cs
            └── HeaderMapExtensions.cs
```

## Core Components

### 1. COSE Message Types

#### CoseSign1 (Single Signer)
```csharp
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

[CborSerializable]
[CborList]
public partial record CoseSign1(
    [CborOrder(0)] byte[] ProtectedHeaders,      // Serialized ProtectedHeaderMap
    [CborOrder(1)] HeaderMap UnprotectedHeaders, // Direct HeaderMap
    [CborOrder(2)] byte[]? Payload,              // null for detached payload
    [CborOrder(3)] byte[] Signature              // Ed25519 signature
) : CborBase, ICoseMessage;
```

#### CoseSign (Multiple Signers)
```csharp
[CborSerializable]
[CborList]
public partial record CoseSign(
    [CborOrder(0)] byte[] ProtectedHeaders,       // Serialized ProtectedHeaderMap
    [CborOrder(1)] HeaderMap UnprotectedHeaders,  // Direct HeaderMap
    [CborOrder(2)] byte[]? Payload,               // null for detached payload
    [CborOrder(3)] CborMaybeIndefList<CoseSignature> Signatures
) : CborBase, ICoseMessage;
```

#### CoseSignature
```csharp
[CborSerializable]
[CborList]
public partial record CoseSignature(
    [CborOrder(0)] byte[] ProtectedHeaders,      // Additional protected headers
    [CborOrder(1)] HeaderMap UnprotectedHeaders, // Additional unprotected headers
    [CborOrder(2)] byte[] Signature              // Ed25519 signature
) : CborBase;
```

### 2. Headers Structure

#### ProtectedHeaderMap
```csharp
// Protected headers are serialized as bytes
public class ProtectedHeaderMap
{
    private readonly byte[] _serializedMap;
    
    public ProtectedHeaderMap(HeaderMap headerMap)
    {
        _serializedMap = headerMap.IsEmpty() 
            ? Array.Empty<byte>()  // Empty bstr for no headers
            : headerMap.ToCbor();  // Serialized map
    }
    
    public byte[] GetBytes() => _serializedMap;
    public HeaderMap Deserialize() => HeaderMap.FromCbor(_serializedMap);
}
```

#### HeaderMap
```csharp
[CborSerializable]
[CborMap]
public partial record HeaderMap : CborBase
{
    // Common headers with COSE label numbers
    [CborProperty(1)] public int? AlgorithmId { get; init; }        // alg
    [CborProperty(2)] public List<Label>? Criticality { get; init; } // crit
    [CborProperty(3)] public string? ContentType { get; init; }      // content type
    [CborProperty(4)] public byte[]? KeyId { get; init; }           // kid
    [CborProperty(5)] public byte[]? InitVector { get; init; }      // IV
    [CborProperty(6)] public byte[]? PartialInitVector { get; init; } // Partial IV
    
    // Additional headers stored as raw CBOR values
    public Dictionary<Label, CborValue>? OtherHeaders { get; init; }
}
```

#### Label (for header keys)
```csharp
[CborSerializable]
[CborUnion]
public abstract partial record Label : CborBase;

[CborSerializable]
public partial record IntLabel(int Value) : Label;

[CborSerializable]
public partial record TextLabel(string Value) : Label;
```

### 3. Signature Structure

#### SigStructure (for creating signature)
```csharp
[CborSerializable]
[CborList]
public partial record SigStructure(
    [CborOrder(0)] string Context,        // "Signature" or "Signature1"
    [CborOrder(1)] byte[] BodyProtected,  // Protected headers from message
    [CborOrder(2)] byte[] SignProtected,  // Empty for Signature1, signer headers for Signature
    [CborOrder(3)] byte[] ExternalAad,    // External additional authenticated data
    [CborOrder(4)] byte[] Payload         // Message payload
) : CborBase;
```

### 4. Enumerations and Constants

```csharp
// Algorithm identifiers
public static class AlgorithmId
{
    public const int EdDSA = -8;           // Pure EdDSA (Cardano's signature algorithm)
    public const int ChaCha20Poly1305 = 24; // For encryption (future use)
}

// Key type identifiers
public static class KeyType
{
    public const int OKP = 1;       // Octet Key Pair (for Ed25519)
    public const int EC2 = 2;       // 2-coordinate Elliptic Curve
    public const int Symmetric = 4; // Symmetric keys
}

// Signature contexts for SigStructure
public static class SigContext
{
    public const string Signature = "Signature";
    public const string Signature1 = "Signature1";
    public const string CounterSignature = "CounterSignature";
}

// Common COSE header labels
public static class HeaderLabels
{
    public const int Algorithm = 1;
    public const int Criticality = 2;
    public const int ContentType = 3;
    public const int KeyId = 4;
    public const int IV = 5;
    public const int PartialIV = 6;
    public const int CounterSignature = 7;
    
    // Custom labels for CIP-8
    public const string Address = "address";
    public const string Hashed = "hashed";
}
```

## API Design

### 1. Signer Interface

```csharp
public interface ICoseSigner
{
    CoseSign1 BuildCoseSign1(
        byte[] payload, 
        PrivateKey signingKey, 
        byte[]? externalAad = null, 
        byte[]? address = null, 
        bool hashPayload = false);
    
    bool VerifyCoseSign1(
        CoseSign1 coseSign1, 
        PublicKey verificationKey, 
        byte[]? externalAad = null, 
        byte[]? address = null);
    
    CoseSign BuildCoseSign(
        byte[] payload,
        List<(PrivateKey key, HeaderMap? headers)> signers,
        byte[]? externalAad = null,
        bool hashPayload = false);
}
```

### 2. Builder Pattern

```csharp
public class CoseSign1Builder
{
    private byte[] _payload = Array.Empty<byte>();
    private byte[]? _externalAad;
    private HeaderMap _protectedHeaders = new();
    private HeaderMap _unprotectedHeaders = new();
    private bool _isPayloadExternal = false;
    private bool _hashPayload = false;
    
    public CoseSign1Builder WithPayload(byte[] payload)
    {
        _payload = payload;
        return this;
    }
    
    public CoseSign1Builder WithPayload(string payload)
    {
        _payload = Encoding.UTF8.GetBytes(payload);
        return this;
    }
    
    public CoseSign1Builder HashPayload()
    {
        _hashPayload = true;
        _unprotectedHeaders = _unprotectedHeaders with 
        { 
            OtherHeaders = (_unprotectedHeaders.OtherHeaders ?? new())
                .Add(new TextLabel(HeaderLabels.Hashed), CborValue.True)
        };
        return this;
    }
    
    public CoseSign1Builder WithExternalAad(byte[] externalAad)
    {
        _externalAad = externalAad;
        return this;
    }
    
    public CoseSign1Builder WithAddress(Address address)
    {
        _protectedHeaders = _protectedHeaders with
        {
            OtherHeaders = (_protectedHeaders.OtherHeaders ?? new())
                .Add(new TextLabel(HeaderLabels.Address), new CborBytes(address.GetBytes()))
        };
        return this;
    }
    
    public CoseSign1Builder WithAlgorithm(int algorithmId = AlgorithmId.EdDSA)
    {
        _protectedHeaders = _protectedHeaders with { AlgorithmId = algorithmId };
        return this;
    }
    
    public CoseSign1Builder WithDetachedPayload()
    {
        _isPayloadExternal = true;
        return this;
    }
    
    public CoseSign1 Build(PrivateKey signingKey)
    {
        // Apply hashing if requested
        var payload = _hashPayload ? HashUtil.Blake2b224(_payload) : _payload;
        
        // Create protected header map
        var protectedHeaderMap = new ProtectedHeaderMap(_protectedHeaders);
        
        // Build SigStructure for signing
        var sigStructure = new SigStructure(
            Context: SigContext.Signature1,
            BodyProtected: protectedHeaderMap.GetBytes(),
            SignProtected: Array.Empty<byte>(), // Empty for Signature1
            ExternalAad: _externalAad ?? Array.Empty<byte>(),
            Payload: payload
        );
        
        // Sign the SigStructure
        var sigBytes = sigStructure.ToCbor();
        var signature = signingKey.Sign(sigBytes);
        
        // Build final CoseSign1
        return new CoseSign1(
            ProtectedHeaders: protectedHeaderMap.GetBytes(),
            UnprotectedHeaders: _unprotectedHeaders,
            Payload: _isPayloadExternal ? null : payload,
            Signature: signature
        );
    }
}
```

### 3. Extension Methods

```csharp
public static class CoseMessageExtensions
{
    // Convert to CIP-8 format with prefix and checksum
    public static string ToCip8Format(this ICoseMessage message)
    {
        var cbor = message.ToCbor();
        var base64url = Base64UrlEncode(cbor);
        
        // Add appropriate prefix based on message type
        var prefix = message switch
        {
            CoseSign1 => "cms_",     // COSE Message Signature1
            CoseSign => "cms1_",     // COSE Message Signature
            _ => throw new NotSupportedException()
        };
        
        // Calculate FNV32a checksum
        var checksum = CalculateFnv32a($"{prefix}{base64url}");
        var checksumBase64 = Base64UrlEncode(checksum);
        
        return $"{prefix}{base64url}_{checksumBase64}";
    }
    
    // Parse from CIP-8 format
    public static ICoseMessage FromCip8Format(string cip8Message)
    {
        // Validate format and checksum
        var parts = cip8Message.Split('_');
        if (parts.Length != 3) throw new FormatException("Invalid CIP-8 format");
        
        var prefix = $"{parts[0]}_";
        var data = parts[1];
        var checksum = parts[2];
        
        // Verify checksum
        var expectedChecksum = CalculateFnv32a($"{prefix}{data}");
        var actualChecksum = Base64UrlDecode(checksum);
        if (!expectedChecksum.SequenceEqual(actualChecksum))
            throw new InvalidOperationException("Invalid checksum");
        
        // Decode based on prefix
        var cbor = Base64UrlDecode(data);
        return prefix switch
        {
            "cms_" => CoseSign1.FromCbor(cbor),
            "cms1_" => CoseSign.FromCbor(cbor),
            _ => throw new NotSupportedException($"Unknown prefix: {prefix}")
        };
    }
    
    // Reconstruct SigStructure for verification
    public static SigStructure GetSigStructure(
        this CoseSign1 message, 
        byte[]? externalAad = null,
        byte[]? payload = null)
    {
        return new SigStructure(
            Context: SigContext.Signature1,
            BodyProtected: message.ProtectedHeaders,
            SignProtected: Array.Empty<byte>(),
            ExternalAad: externalAad ?? Array.Empty<byte>(),
            Payload: payload ?? message.Payload ?? throw new ArgumentException("Payload required")
        );
    }
}
```

## Implementation Plan

### Phase 1: Core Models (Week 1)
- [ ] Implement CBOR-serializable models
- [ ] Create enumerations and constants
- [ ] Implement Headers and HeaderMap
- [ ] Add CBOR serialization attributes

### Phase 2: Signing Infrastructure (Week 2)
- [ ] Implement SigStructure
- [ ] Create ICoseSigner interface
- [ ] Implement EdDsaCoseSigner
- [ ] Add signature generation logic

### Phase 3: Builder Pattern (Week 3)
- [ ] Implement CoseSign1Builder
- [ ] Implement CoseSignBuilder
- [ ] Add fluent API methods
- [ ] Create factory methods

### Phase 4: Integration & Extensions (Week 4)
- [ ] Integrate with Chrysalis.Wallet keys
- [ ] Add extension methods
- [ ] Implement CIP-8 format encoding
- [ ] Add address binding support

### Phase 5: Testing & Documentation (Week 5)
- [ ] Unit tests for all components
- [ ] Integration tests with wallet
- [ ] Performance benchmarks
- [ ] API documentation
- [ ] Usage examples

## Integration Points

### 1. Chrysalis.Wallet
- Use existing `PrivateKey` and `PublicKey` classes
- Leverage `Address` class for address binding
- Integrate with key derivation paths

### 2. Chrysalis.Cbor
- Use `[CborSerializable]` attributes
- Leverage existing CBOR serialization
- Ensure compatibility with `CborValue` types

### 3. Chrysalis.Tx
- Potential future integration for transaction message signing
- Metadata message signing support

## Security Considerations

1. **Key Protection**: Never expose private keys in logs or errors
2. **Payload Validation**: Validate payload size limits
3. **Header Validation**: Ensure critical headers are protected
4. **Signature Verification**: Always verify full SigStructure
5. **External AAD**: Document when external AAD should be used

## Performance Considerations

1. **CBOR Caching**: Cache serialized CBOR for repeated operations
2. **Lazy Evaluation**: Defer expensive operations until needed
3. **Memory Efficiency**: Use spans/memory where appropriate
4. **Parallel Verification**: Support parallel signature verification

## Example Usage

```csharp
using Chrysalis.Wallet.CIPs.CIP8;
using Chrysalis.Wallet.Models.Keys;

// 1. Simple message signing
var privateKey = wallet.GetPrivateKey("m/1852'/1815'/0'/0/0");
var address = wallet.GetAddress(0);
var message = "Hello, Cardano!";

var signedMessage = new CoseSign1Builder()
    .WithPayload(message)
    .WithAddress(address)
    .WithAlgorithm(AlgorithmId.EdDSA)
    .Build(privateKey);

// 2. Verification
var signer = new EdDsaCoseSigner();
var publicKey = privateKey.GetPublicKey();
var isValid = signer.VerifyCoseSign1(signedMessage, publicKey);

// 3. Export to CIP-8 format (base64url with prefix)
var cip8Message = signedMessage.ToCip8Format(); // "cms_..." format

// 4. Advanced: Signing with external AAD and hashed payload
var externalData = Encoding.UTF8.GetBytes("context-specific-data");
var largePayload = File.ReadAllBytes("large-file.dat");

var advancedMessage = new CoseSign1Builder()
    .WithPayload(largePayload)
    .HashPayload()  // Hashes payload with Blake2b224
    .WithExternalAad(externalData)
    .WithAddress(address)
    .Build(privateKey);

// 5. Detached payload signing (payload not included in message)
var detachedMessage = new CoseSign1Builder()
    .WithPayload("Secret message")
    .WithDetachedPayload()
    .WithAddress(address)
    .Build(privateKey);

// Verification requires providing the payload separately
var isValidDetached = signer.VerifyCoseSign1(
    detachedMessage, 
    publicKey, 
    payload: Encoding.UTF8.GetBytes("Secret message")
);

// 6. Import from CIP-8 format
var importedMessage = CoseSign1.FromCip8Format("cms_...");
```

## Testing Strategy

1. **Unit Tests**
   - Model serialization/deserialization
   - Header construction
   - Signature generation/verification
   - Builder pattern functionality

2. **Integration Tests**
   - End-to-end signing flows
   - Wallet integration
   - Cross-library compatibility

3. **Compatibility Tests**
   - Test against CardanoSharp test vectors
   - Test against Emurgo message-signing examples
   - Verify CIP-8 format compliance

## Future Enhancements

1. **Hardware Wallet Support**
   - Ledger integration
   - Trezor integration

2. **Additional Algorithms**
   - Support for future algorithms beyond EdDSA

3. **Encryption Support**
   - COSEEncrypt for encrypted messages
   - Key agreement protocols

4. **Advanced Features**
   - Batch signing
   - Delegated signing
   - Multi-party signatures