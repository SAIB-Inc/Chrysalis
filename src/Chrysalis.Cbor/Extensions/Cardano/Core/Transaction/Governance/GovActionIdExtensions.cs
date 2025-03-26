using Chrysalis.Cbor.Types.Cardano.Core.Governance;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Governance;

public static class GovActionIdExtensions
{
    public static byte[] TransactionId(this GovActionId self) => self.TransactionId;

    public static int GovActionIndex(this GovActionId self) => self.GovActionIndex;
}