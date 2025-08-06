# CIP-8 Message Signing Implementation Details

## Overview

This document provides the technical implementation details for Chrysalis's CIP-8 (Message Signing), documenting the exact formats, structures, and cryptographic operations as implemented and verified against the Rust reference implementation.

## 1. COSE_Sign1 Message Structure

The implementation follows RFC 8152 for COSE (CBOR Object Signing and Encryption) with specific adaptations for Cardano as defined in CIP-8.

### 1.1 CBOR Array Structure

```
COSE_Sign1 = [
    protected : bstr,        // Protected headers (CBOR-encoded map)
    unprotected : map,       // Unprotected headers
    payload : bstr / nil,    // Message payload (nil for detached)
    signature : bstr         // EdDSA signature (64 bytes)
]
```

CBOR diagnostic notation:
```
84                        # array(4)
   58 XX                  # bytes(protected_headers)
   A1                     # map(1) - unprotected headers
   58 XX / F6            # bytes(payload) or null
   58 40                 # bytes(64) - signature
```

## 2. Header Structure

### 2.1 Protected Headers

Protected headers are CBOR-encoded and wrapped as a byte string. The map MUST contain:

```cbor
{
    1: -8,                    # Algorithm: EdDSA
    -8: h'...'               # Address bytes (29 bytes for payment addresses)
}
```

Key definitions:
- `1` (alg): Algorithm identifier, MUST be `-8` (EdDSA)
- `-8`: Address binding (custom label for Cardano addresses)

### 2.2 Unprotected Headers

Unprotected headers are a plain CBOR map:

```cbor
{
    "hashed": true/false     # Indicates if payload is hashed
}
```

### 2.3 HeaderMap Implementation Note

**Important**: The `HeaderMap` class uses custom CBOR serialization/deserialization because COSE headers use a union type for labels (int | tstr) which is not currently supported by Chrysalis.Cbor code generation.

COSE allows map keys to be either integers or strings:
```cbor
{
    1: -8,                    # Integer key (algorithm)
    "hashed": false,          # String key (custom header)
    -8: h'...'               # Negative integer key (address)
}
```

Current implementation uses manual `CborWriter`/`CborReader` to handle this:
```csharp
// Manual serialization required for union types
public static void Write(CborWriter writer, HeaderMap data)
{
    writer.WriteStartMap(data._headers.Count);
    foreach (var (key, value) in data._headers)
    {
        // Write key as int or string based on type
        if (key is int intKey)
            writer.WriteInt32(intKey);
        else if (key is string strKey)
            writer.WriteTextString(strKey);
        // ... handle value serialization
    }
    writer.WriteEndMap();
}
```

**TODO**: Add custom base type support to Chrysalis.Cbor code generation (see GitHub issue #267)

## 3. Sig_structure Format

**CRITICAL**: For `Signature1` context, the Sig_structure MUST contain exactly 4 elements (not 5).

### 3.1 Structure Definition

```
Sig_structure = [
    context : "Signature1",           # Context string
    body_protected : bstr,            # Protected headers from message
    external_aad : bstr,              # External additional data (empty if none)
    payload : bstr                    # Payload to sign
]
```

### 3.2 CBOR Encoding

```cbor
84                                    # array(4)
   6A                                 # text(10)
      5369676E617475726531            # "Signature1"
   58 XX                              # bytes(body_protected)
   40                                 # bytes(0) - empty external_aad
   58 XX                              # bytes(payload)
```

**Note**: The `sign_protected` field is NOT included for `Signature1` context. It would be the third element for `Signature` or `CounterSignature` contexts.

## 4. Key Format Support

### 4.1 Shelley Extended Signing Keys

The implementation supports Cardano Shelley extended signing keys (128 bytes):

```
Bytes 0-63:   Extended Ed25519 private key (64 bytes)
Bytes 64-95:  Chain code (32 bytes)
Bytes 96-127: Public key (32 bytes)
```

### 4.2 Verification Keys

Extended verification keys (64 bytes):
```
Bytes 0-31:  Public key (32 bytes)
Bytes 32-63: Chain code (32 bytes)
```

### 4.3 Key File Format

Cardano key files use JSON with CBOR hex encoding:

```json
{
    "type": "PaymentSigningKeyShelley_ed25519_bip32",
    "description": "Payment Signing Key",
    "cborHex": "5880..." // 128 bytes hex encoded
}
```

## 5. Signing Process

### 5.1 Algorithm

1. Construct protected headers map with algorithm and address
2. CBOR-encode protected headers and wrap as byte string
3. Create unprotected headers map with "hashed" field
4. Build Sig_structure with 4 elements for Signature1 context
5. CBOR-encode Sig_structure to get bytes to sign
6. Sign using Ed25519 (first 64 bytes of extended key if using Shelley format)
7. Assemble COSE_Sign1 array with all components

### 5.2 Code Example

```csharp
// Build protected headers
var protectedWriter = new CborWriter(CborConformanceMode.Strict);
protectedWriter.WriteStartMap(2);
protectedWriter.WriteInt32(1);  // algorithm label
protectedWriter.WriteInt32(-8); // EdDSA
protectedWriter.WriteInt32(-8); // address label
protectedWriter.WriteByteString(address.ToBytes());
protectedWriter.WriteEndMap();
var protectedHeaderBytes = protectedWriter.Encode();

// Build Sig_structure (4 elements for Signature1)
var sigStructure = new SigStructure(
    Context: "Signature1",
    BodyProtected: protectedHeaderBytes,
    SignProtected: [], // Not included in CBOR for Signature1
    ExternalAad: [],
    Payload: payload
);

// Sign
var sigBytes = sigStructure.ToCbor();
var signature = privateKey.Sign(sigBytes);
```

## 6. CIP-8 Format Encoding

### 6.1 Structure

```
cms_<base64url(cbor_bytes)><base64url(checksum)>
```

### 6.2 Checksum Calculation

- Algorithm: FNV-32a (32-bit Fowler-Noll-Vo hash)
- Input: Raw CBOR bytes of the COSE_Sign1 message
- Output: 4-byte checksum, base64url-encoded

### 6.3 Implementation

```csharp
public static string ToCip8Format(byte[] cborBytes)
{
    var checksum = ComputeFnv32a(cborBytes);
    var encoded = Base64UrlEncode(cborBytes);
    var checksumEncoded = Base64UrlEncode(checksum);
    return $"cms_{encoded}{checksumEncoded}";
}
```

## 7. Verification Process

### 7.1 Algorithm

1. Parse COSE_Sign1 message from CBOR
2. Extract protected headers, unprotected headers, payload, and signature
3. Reconstruct Sig_structure (4 elements for Signature1)
4. CBOR-encode Sig_structure
5. Verify signature using Ed25519 public key

### 7.2 Verification Code

```csharp
public bool VerifyCoseSign1(CoseSign1 message, PublicKey publicKey)
{
    // Reconstruct Sig_structure
    var sigStructure = new SigStructure(
        Context: "Signature1",
        BodyProtected: message.ProtectedHeaders.ToBytes(),
        SignProtected: [], // Not included for Signature1
        ExternalAad: message.ExternalAad ?? [],
        Payload: message.Payload ?? []
    );
    
    var sigBytes = sigStructure.ToCbor();
    return publicKey.Verify(sigBytes, message.Signature);
}
```

## 8. Compatibility Verification

### 8.1 Test Results

Our implementation has been verified against:

1. **Rust cardano-message-signing library**: ✅ Full compatibility
   - Signatures can be parsed by Rust implementation
   - Cryptographic verification passes

2. **Eternl Wallet**: ✅ Structure compatible
   - Both use same COSE_Sign1 format
   - Both include address in protected headers
   - Signature bytes differ (normal - different random k values)

### 8.2 Common Implementation Pitfalls Avoided

1. **Sig_structure Array Length**: Correctly uses 4 elements for Signature1
2. **Protected Headers**: Properly CBOR-encoded then wrapped as byte string
3. **Algorithm Label**: Uses integer `-8` for EdDSA, not string
4. **Key Format**: Handles 128-byte Shelley extended keys correctly

## 9. CLI Tool Usage

### 9.1 Signing Messages

```bash
dotnet run sign --skey payment.skey --vkey payment.vkey --payload message.txt

# Output:
# Address: addr1vyyl69u8el0mwfjzdytxpmcwlstnpkchts5x2k3r768ds5qge6cps
# CIP-8 Format: cms_hFgjogEnJ1g...
# CBOR Hex: 845823a20127...
```

### 9.2 Verifying Signatures

```bash
dotnet run verify --vkey payment.vkey --signature cms_hFgjogEnJ1g... --payload message.txt

# Output:
# Verification: ✓ PASSED
```

## 10. Test Vectors

### 10.1 Basic Signature (Verified with Rust Implementation)

```
Public Key (hex): 27498ed57647a8ebc9c215c0981cf33ee9bbd5fb71006a02a411c8e8a849d80a
Address: addr1vyyl69u8el0mwfjzdytxpmcwlstnpkchts5x2k3r768ds5qge6cps
Payload: "STAR 18380972457 to addr1qy43te4lpjvaa3et5l7493kd80crj6cvkn6gmn0d2l6tt53uqtx66gla26v9jr4gxdgkqfp0xj0fjmxkys4uz4ka046sep0t3k 31a6bab50a84b8439adcfb786bb2020f6807e6e8fda629b424110fc7bb1c6b8b"

COSE_Sign1 CBOR (hex): 845823a2012727581d6109fd1787cfdfb72642691660ef0efc1730db175c28655a23f68ed850a166686173686564f458bc5354415220...

Sig_structure CBOR (hex): 846a5369676e6174757265315823a2012727581d6109fd1787cfdfb72642691660ef0efc1730db175c28655a23f68ed8504058bc...

✅ Verified by Rust cardano-message-signing library
```

## 11. Security Considerations

### 11.1 Key Management

- Private keys are only held in memory during signing
- Keys are read directly from file and not logged
- Sensitive data cleared after use

### 11.2 Signature Validation

- Algorithm in protected headers verified to be EdDSA (-8)
- Address binding validated against expected address
- Payload integrity checked

## 12. References

- [CIP-8: Message Signing](https://cips.cardano.org/cips/cip8/)
- [RFC 8152: CBOR Object Signing and Encryption (COSE)](https://tools.ietf.org/html/rfc8152)
- [cardano-message-signing (Rust)](https://github.com/Emurgo/message-signing)
- [CBOR Specification (RFC 7049)](https://tools.ietf.org/html/rfc7049)