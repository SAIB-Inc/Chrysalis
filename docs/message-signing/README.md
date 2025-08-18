# CIP-8 Message Signing Documentation

## Overview

This directory contains the complete documentation for Chrysalis's implementation of CIP-8 (Message Signing) for Cardano.

## Documents

### [CIP8_SPECIFICATION.md](./CIP8_SPECIFICATION.md)
The original design specification outlining the architecture, components, and API design for the CIP-8 implementation.

### [IMPLEMENTATION.md](./IMPLEMENTATION.md)
Detailed technical documentation of the actual implementation, including:
- CBOR structures and encoding
- Cryptographic operations
- Key format handling
- Compatibility verification results
- Test vectors

## Quick Links

- **Source Code**: [`src/Chrysalis.Wallet/CIPs/CIP8/`](../../src/Chrysalis.Wallet/CIPs/CIP8/)
- **CLI Tool**: [`src/Chrysalis.Wallet.Cli/`](../../src/Chrysalis.Wallet.Cli/)
- **Unit Tests**: [`src/Chrysalis.Wallet.Test/CIPs/CIP8Tests.cs`](../../src/Chrysalis.Wallet.Test/CIPs/CIP8Tests.cs)

## Status

✅ **Implemented and Verified**

The implementation has been successfully verified against:
- Rust `cardano-message-signing` library (cryptographic verification passes)
- Eternl wallet (structure compatible)
- CIP-8 specification compliance

## Key Features

- **COSE_Sign1** message format (RFC 8152)
- **EdDSA (Ed25519)** signatures
- **Address binding** for proving address ownership
- **Shelley extended key** support (128-byte format)
- **CIP-8 format** encoding with FNV-32a checksum
- **CLI tool** for signing and verifying messages

## Usage Example

### Library Usage

```csharp
using Chrysalis.Wallet.CIPs.CIP8.Builders;

var signedMessage = new CoseSign1Builder()
    .WithPayload("Hello, Cardano!")
    .WithAddress(address)
    .Build(privateKey);

var cip8Format = signedMessage.ToCip8Format();
```

### CLI Usage

```bash
# Sign a message
dotnet run --project src/Chrysalis.Wallet.Cli sign \
    --skey payment.skey \
    --vkey payment.vkey \
    --payload message.txt

# Output:
# Address: addr1vyyl69u8el0mwfjzdytxpmcwlstnpkchts5x2k3r768ds5qge6cps
# CIP-8 Format: cms_hFgjogEnJ1g...
# CBOR Hex: 845823a20127...
```

## Important Implementation Notes

1. **Sig_structure for Signature1**: Must contain exactly 4 elements (not 5)
2. **Protected Headers**: Must be CBOR-encoded then wrapped as byte string
3. **Key Format**: Properly handles 128-byte Shelley extended keys
4. **Algorithm Identifier**: Uses integer `-8` for EdDSA

## References

- [CIP-8: Message Signing](https://cips.cardano.org/cips/cip8/)
- [RFC 8152: COSE](https://tools.ietf.org/html/rfc8152)
- [Rust Implementation](https://github.com/Emurgo/message-signing)