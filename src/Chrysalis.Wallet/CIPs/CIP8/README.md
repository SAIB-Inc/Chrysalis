# CIP-8 Message Signing Implementation

This module implements CIP-8 (Message Signing) for the Chrysalis wallet ecosystem, providing COSE-based message signing compatible with the Cardano ecosystem.

## Documentation

For complete documentation, see:
- [CIP-8 Specification](../../../../docs/message-signing/CIP8_SPECIFICATION.md)
- [Implementation Details](../../../../docs/message-signing/IMPLEMENTATION.md)

## Quick Start

```csharp
using Chrysalis.Wallet.CIPs.CIP8.Builders;
using Chrysalis.Wallet.CIPs.CIP8.Extensions;

// Sign a message
var signedMessage = new CoseSign1Builder()
    .WithPayload("Hello, Cardano!")
    .WithAddress(address)
    .Build(privateKey);

// Export to CIP-8 format
var cip8String = signedMessage.ToCip8Format(); // "cms_..."

// Verify
var signer = new EdDsaCoseSigner();
bool isValid = signer.VerifyCoseSign1(signedMessage, publicKey);
```

## CLI Tool

A command-line tool is available at `Chrysalis.Wallet.Cli` for signing and verifying messages:

```bash
# Sign a message
dotnet run --project src/Chrysalis.Wallet.Cli sign \
    --skey payment.skey \
    --vkey payment.vkey \
    --payload message.txt

# Verify a signature
dotnet run --project src/Chrysalis.Wallet.Cli verify \
    --vkey payment.vkey \
    --signature cms_... \
    --payload message.txt
```

## Compatibility

✅ Verified compatible with:
- Rust cardano-message-signing library
- Eternl wallet
- CIP-8 specification