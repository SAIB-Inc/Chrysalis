using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Models.Addresses;

public record Credential(
    CredentialType Type,
    byte[] Hash
);