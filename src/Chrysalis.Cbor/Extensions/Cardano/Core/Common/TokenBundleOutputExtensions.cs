using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Common;

public static class TokenBundleOutputExtensions
{
    public static Dictionary<byte[], ulong> ToDict(this TokenBundleOutput self) => self.Value;
}