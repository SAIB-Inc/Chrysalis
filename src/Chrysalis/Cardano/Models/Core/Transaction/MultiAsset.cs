using Chrysalis.Cardano.Models.Cbor;

namespace Chrysalis.Cardano.Models.Core.Transaction;

public record MultiAsset(Dictionary<CborBytes, TokenBundle> Value) : CborMap<CborBytes, TokenBundle>(Value);