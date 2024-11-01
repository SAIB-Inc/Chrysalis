using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Core;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(TokenBundleOutput),
    typeof(TokenBundleMint),
])]
public interface TokenBundle : ICbor;

public record TokenBundleOutput(Dictionary<CborBytes, CborUlong> Value) : CborMap<CborBytes, CborUlong>(Value), TokenBundle;

public record TokenBundleMint(Dictionary<CborBytes, CborLong> Value) : CborMap<CborBytes, CborLong>(Value), TokenBundle;