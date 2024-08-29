using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Map)]
public record MultiAsset(CborMap<CborBytes, TokenBundle> Value) : ICbor;