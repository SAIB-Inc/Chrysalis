using Chrysalis.Cardano.Core.Types.Block.Transaction.Output;

namespace Chrysalis.Cardano.Core.Extensions;

public static class TokenBundleExtension
{
    public static Dictionary<string, ulong> TokenBundle(this TokenBundleOutput tokenBundleOutput)
    {
        return tokenBundleOutput.Value
            .ToDictionary(kvp => Convert.ToHexString(kvp.Key.Value).ToLowerInvariant(), kvp => kvp.Value.Value);
    }

    public static Dictionary<string, long> TokenBundle(this TokenBundleMint tokenBundleMint)
    {
        return tokenBundleMint.Value
            .ToDictionary(kvp => Convert.ToHexString(kvp.Key.Value).ToLowerInvariant(), kvp => kvp.Value.Value);
    }
}