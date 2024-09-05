using Chrysalis.Cardano.Models.Cbor;

namespace Chrysalis.Cardano.Models.Core;

public record MultiAsset(Dictionary<CborBytes, TokenBundle> Value) : CborMap<CborBytes, TokenBundle>(Value);