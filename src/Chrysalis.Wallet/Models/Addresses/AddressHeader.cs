using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Models.Addresses;

public record AddressHeader(
    AddressType Type,
    NetworkType Network
);