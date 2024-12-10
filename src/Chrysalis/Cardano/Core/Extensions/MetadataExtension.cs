using Chrysalis.Cardano.Core.Types.Block.Transaction;

namespace Chrysalis.Cardano.Core.Extensions;

public static class MetadataExtension
{
    private const string CIP25_KEY = "721";

    public static IEnumerable<string>? PolicyIds(this Metadata metadata)
        => metadata.Value.Keys.Select(key => Convert.ToHexString(key.Value).ToLowerInvariant());

    public static bool IsCIP25(this Metadata metadata)
        => metadata.Value.ContainsKey(new MetadataText(CIP25_KEY));
}