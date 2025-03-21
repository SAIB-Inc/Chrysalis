using Chrysalis.Tx.Models.Enums;

namespace Chrysalis.Tx.Models.Addresses;

public record Credential(
    CredentialType Type,
    byte[] Hash
);