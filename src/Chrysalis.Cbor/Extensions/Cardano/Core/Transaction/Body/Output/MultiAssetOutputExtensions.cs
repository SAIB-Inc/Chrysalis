using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.Body.Output;

public static class MultiAssetOutputExtensions
{
    public static Dictionary<byte[], TokenBundleOutput> ToDict(this MultiAssetOutput self) => self.Value;
}