using Chrysalis.Wallet.Models.Enums;

namespace Chrysalis.Wallet.Addresses;

public record AddressHeader(
    AddressType Type,
    NetworkType Network
);