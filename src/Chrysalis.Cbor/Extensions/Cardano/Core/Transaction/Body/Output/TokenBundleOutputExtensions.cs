using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.Body.Output;

public static class TokenBundleOutputExtensions
{
    public static Dictionary<byte[], ulong> ToDict(this TokenBundleOutput self) => self.Value;
}