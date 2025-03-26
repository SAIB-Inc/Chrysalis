using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Addresses;

public record Credential(
    CredentialType Type,
    byte[] Hash
);