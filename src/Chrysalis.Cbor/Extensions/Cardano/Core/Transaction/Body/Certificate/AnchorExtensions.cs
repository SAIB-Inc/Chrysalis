using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Transaction.Body.Certificate;

public static class AnchorExtensions
{
    public static string Url(this Anchor self) => self.AnchorUrl;

    public static byte[] DataHash(this Anchor self) => self.AnchorDataHash;
}