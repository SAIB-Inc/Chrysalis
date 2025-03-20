using Chrysalis.Cbor.Cardano.Types.Block.Transaction;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Body.Certificate;

public static class AnchorExtensions
{
    public static string Url(this Anchor self) => self.AnchorUrl;

    public static byte[] DataHash(this Anchor self) => self.AnchorDataHash;
}