using Chrysalis.Cbor.Types.Cardano.Core.Certificates;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction;

public static class RewardAccountExtensions
{
    public static byte[] Value(this RewardAccount self) => self.Value;
}