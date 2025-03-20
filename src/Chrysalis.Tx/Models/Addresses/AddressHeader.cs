using Chrysalis.Tx.Models.Enums;

namespace Chrysalis.Tx.Models.Addresses;

public record AddressHeader(
    AddressType Type,
    NetworkType Network
);