# WA6: Stricter Ed25519 verification

`Ed25519.Verify` and the wallet's `PublicKey.Verify` now match the Cardano node's cofactorless Ed25519 verifier. Correctly produced Ed25519 signatures are unaffected.

- RFC 8032 §5.1.7 requires `0 <= S < L` and canonical encodings for the public key `A` and signature point `R`. Previously accepted non-canonical signatures now return `false`.
- All eight small-order public keys and small-order `R` values are rejected. Re-sign rejected messages with a valid key and a standard Ed25519 signer rather than changing the signature bytes.
- Verification uses call-local stack scratch and streams SHA-512 for larger messages. The managed SHA-512 and byte helpers were replaced by `SHA512`, `IncrementalHash`, and `CryptographicOperations`.
- Key derivation and signing clear secret intermediates, including the scalar digits in `ge_scalarmult_base` and the signing buffers in `sign.cs`.
