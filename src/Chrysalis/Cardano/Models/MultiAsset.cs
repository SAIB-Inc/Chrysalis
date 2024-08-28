namespace Chrysalis.Cardano.Models;

public record MultiAsset(Dictionary<CborBytes, TokenBundle> Value) 
    : CborMap<CborBytes, TokenBundle>(Value);